using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Don pending chat media qua TTL de tranh orphan object trong private bucket.
/// </summary>
public sealed class PendingMediaCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingMediaCleanupWorker> _logger;

    public PendingMediaCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingMediaCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan interval;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<MediaStorageOptions>>().Value;
                interval = GetCleanupInterval(options);

                await RunCleanupBatchAsync(scope.ServiceProvider, options, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending media cleanup worker gap loi, se thu lai sau 1 phut.");
                interval = TimeSpan.FromMinutes(1);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunCleanupBatchAsync(
        IServiceProvider serviceProvider,
        MediaStorageOptions options,
        CancellationToken cancellationToken)
    {
        var dbContext = serviceProvider.GetRequiredService<AppDbContext>();
        var storageService = serviceProvider.GetRequiredService<IMediaStorageService>();
        var ttl = GetPendingMediaTtl(options);
        var cutoff = DateTime.UtcNow.Subtract(ttl);
        var batchSize = Math.Clamp(options.PendingCleanupBatchSize, 1, 1000);
        var startedAt = DateTime.UtcNow;

        await RecoverExpiredReservationsAsync(
            serviceProvider,
            dbContext,
            options,
            batchSize,
            cancellationToken);

        var candidates = await dbContext.MediaAssets
            .AsNoTracking()
            .Where(asset =>
                asset.Scope == MediaScope.ChatAttachment &&
                asset.Status == MediaAssetStatus.Pending &&
                asset.RoomId == null &&
                asset.DeletedAt == null &&
                asset.CreatedAt < cutoff)
            .OrderBy(asset => asset.CreatedAt)
            .Select(asset => new PendingMediaCleanupCandidate(
                asset.Id,
                asset.BucketName,
                asset.StorageKey,
                asset.CreatedAt))
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var claimedCount = 0;
        var deletedCount = 0;
        var failedCount = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await TryClaimPendingMediaAsync(dbContext, candidate.Id, cutoff, cancellationToken))
            {
                continue;
            }

            claimedCount++;

            try
            {
                await storageService.DeleteObjectAsync(
                    candidate.BucketName,
                    candidate.StorageKey,
                    cancellationToken);

                await MarkDeletedAsync(dbContext, candidate.Id, cancellationToken);
                deletedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogWarning(
                    ex,
                    "Khong xoa duoc pending media object. MediaId={MediaId}; Bucket={Bucket}; Key={Key}",
                    candidate.Id,
                    candidate.BucketName,
                    candidate.StorageKey);

                await RevertClaimAsync(dbContext, candidate.Id, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Pending media cleanup hoan tat. Candidate={CandidateCount}; Claimed={ClaimedCount}; Deleted={DeletedCount}; Failed={FailedCount}; ElapsedMs={ElapsedMs}",
            candidates.Count,
            claimedCount,
            deletedCount,
            failedCount,
            (DateTime.UtcNow - startedAt).TotalMilliseconds);
    }

    private async Task RecoverExpiredReservationsAsync(
        IServiceProvider serviceProvider,
        AppDbContext dbContext,
        MediaStorageOptions options,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var reservationCutoff = DateTime.UtcNow.Subtract(GetReservationTtl(options));
        var candidates = await dbContext.MediaAssets
            .AsNoTracking()
            .Where(asset =>
                asset.Scope == MediaScope.ChatAttachment &&
                asset.Status == MediaAssetStatus.Reserved &&
                asset.RoomId == null &&
                asset.DeletedAt == null &&
                asset.ReservedAt != null &&
                asset.ReservedAt < reservationCutoff)
            .OrderBy(asset => asset.ReservedAt)
            .Select(asset => new ExpiredReservationCandidate(
                asset.Id,
                asset.ReservedByMessageId,
                asset.ReservedAt!.Value))
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return;

        var messageIds = candidates
            .Select(candidate => candidate.MessageId)
            .Where(messageId => !string.IsNullOrWhiteSpace(messageId))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var mongoClient = serviceProvider.GetRequiredService<IMongoClient>();
        var messagesCollection = mongoClient
            .GetDatabase("ChatAppDB_Mongo")
            .GetCollection<Message>("messages");
        var persistedMessages = messageIds.Count == 0
            ? []
            : await messagesCollection
                .Find(message => messageIds.Contains(message.Id))
                .ToListAsync(cancellationToken);
        var persistedById = persistedMessages
            .ToDictionary(message => message.Id, StringComparer.Ordinal);
        var repairedCount = 0;
        var releasedCount = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(candidate.MessageId) &&
                persistedById.TryGetValue(candidate.MessageId, out var persistedMessage))
            {
                repairedCount += await RepairPersistedReservationAsync(
                    dbContext,
                    candidate,
                    persistedMessage,
                    cancellationToken);
                continue;
            }

            releasedCount += await ReleaseExpiredReservationAsync(
                dbContext,
                candidate,
                reservationCutoff,
                cancellationToken);
        }

        _logger.LogInformation(
            "Reservation cleanup hoan tat. Candidate={CandidateCount}; Repaired={RepairedCount}; Released={ReleasedCount}",
            candidates.Count,
            repairedCount,
            releasedCount);
    }

    private static async Task<bool> TryClaimPendingMediaAsync(
        AppDbContext dbContext,
        Guid mediaId,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var affectedRows = await dbContext.MediaAssets
            .Where(asset =>
                asset.Id == mediaId &&
                asset.Scope == MediaScope.ChatAttachment &&
                asset.Status == MediaAssetStatus.Pending &&
                asset.RoomId == null &&
                asset.DeletedAt == null &&
                asset.CreatedAt < cutoff)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    asset => asset.Status,
                    MediaAssetStatus.CleanupInProgress),
                cancellationToken);

        return affectedRows == 1;
    }

    private static Task MarkDeletedAsync(
        AppDbContext dbContext,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return dbContext.MediaAssets
            .Where(asset =>
                asset.Id == mediaId &&
                asset.Status == MediaAssetStatus.CleanupInProgress)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(asset => asset.Status, MediaAssetStatus.Deleted)
                    .SetProperty(asset => asset.DeletedAt, now),
                cancellationToken);
    }

    private static Task RevertClaimAsync(
        AppDbContext dbContext,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        return dbContext.MediaAssets
            .Where(asset =>
                asset.Id == mediaId &&
                asset.Status == MediaAssetStatus.CleanupInProgress)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(asset => asset.Status, MediaAssetStatus.Pending),
                cancellationToken);
    }

    private static TimeSpan GetCleanupInterval(MediaStorageOptions options)
    {
        return TimeSpan.FromMinutes(Math.Max(1, options.PendingCleanupIntervalMinutes));
    }

    private static TimeSpan GetPendingMediaTtl(MediaStorageOptions options)
    {
        return TimeSpan.FromHours(Math.Max(1, options.PendingMediaTtlHours));
    }

    private static TimeSpan GetReservationTtl(MediaStorageOptions options)
    {
        return TimeSpan.FromHours(Math.Max(1, options.ReservationTtlHours));
    }

    private static Task<int> RepairPersistedReservationAsync(
        AppDbContext dbContext,
        ExpiredReservationCandidate candidate,
        Message persistedMessage,
        CancellationToken cancellationToken)
    {
        var attachedAt = persistedMessage.AcceptedAt ?? persistedMessage.CreatedAt;
        return dbContext.MediaAssets
            .Where(asset =>
                asset.Id == candidate.Id &&
                asset.Scope == MediaScope.ChatAttachment &&
                asset.Status == MediaAssetStatus.Reserved &&
                asset.ReservedByMessageId == candidate.MessageId &&
                asset.RoomId == null &&
                asset.DeletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(asset => asset.Status, MediaAssetStatus.Attached)
                    .SetProperty(asset => asset.RoomId, persistedMessage.RoomId)
                    .SetProperty(asset => asset.MessageId, persistedMessage.Id)
                    .SetProperty(asset => asset.AttachedAt, attachedAt)
                    .SetProperty(asset => asset.ReservedByMessageId, (string?)null)
                    .SetProperty(asset => asset.ReservedAt, (DateTime?)null),
                cancellationToken);
    }

    private static Task<int> ReleaseExpiredReservationAsync(
        AppDbContext dbContext,
        ExpiredReservationCandidate candidate,
        DateTime reservationCutoff,
        CancellationToken cancellationToken)
    {
        return dbContext.MediaAssets
            .Where(asset =>
                asset.Id == candidate.Id &&
                asset.Scope == MediaScope.ChatAttachment &&
                asset.Status == MediaAssetStatus.Reserved &&
                asset.ReservedByMessageId == candidate.MessageId &&
                asset.RoomId == null &&
                asset.DeletedAt == null &&
                asset.ReservedAt < reservationCutoff)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(asset => asset.Status, MediaAssetStatus.Pending)
                    .SetProperty(asset => asset.ReservedByMessageId, (string?)null)
                    .SetProperty(asset => asset.ReservedAt, (DateTime?)null),
                cancellationToken);
    }

    private sealed record PendingMediaCleanupCandidate(
        Guid Id,
        string BucketName,
        string StorageKey,
        DateTime CreatedAt);

    private sealed record ExpiredReservationCandidate(
        Guid Id,
        string? MessageId,
        DateTime ReservedAt);
}
