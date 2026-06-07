using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Bao dam mot tap attachment chi thuoc ve mot tin nhan trong giai doan xu ly bat dong bo.
/// </summary>
public sealed class ChatMediaReservationService : IChatMediaReservationService
{
    private const int MaxTransactionAttempts = 3;

    private readonly AppDbContext _dbContext;
    private readonly ILogger<ChatMediaReservationService> _logger;

    public ChatMediaReservationService(
        AppDbContext dbContext,
        ILogger<ChatMediaReservationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatMediaReservationResult> ReserveAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        DateTime reservedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedIds = NormalizeMediaIds(mediaIds);
        ValidateMessageContext(ownerUserId, roomId, messageId);

        for (var attempt = 1; attempt <= MaxTransactionAttempts; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var assetsAssignedToMessage = _dbContext.MediaAssets.Where(asset =>
                    asset.Scope == MediaScope.ChatAttachment &&
                    ((asset.Status == MediaAssetStatus.Reserved &&
                      asset.ReservedByMessageId == messageId) ||
                     (asset.Status == MediaAssetStatus.Attached &&
                      asset.MessageId == messageId)));

                if (normalizedIds.Count > 0)
                {
                    assetsAssignedToMessage = assetsAssignedToMessage
                        .Where(asset => !normalizedIds.Contains(asset.Id));
                }

                if (await assetsAssignedToMessage.AnyAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ChatMediaReservationResult.Rejected(
                        "Tap attachment khong khop voi lan gui truoc cua cung messageId.");
                }

                if (normalizedIds.Count == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ChatMediaReservationResult.Success(false);
                }

                var assets = await _dbContext.MediaAssets
                    .Where(asset => normalizedIds.Contains(asset.Id))
                    .ToListAsync(cancellationToken);

                var rejectionReason = ValidateCommonState(
                    assets,
                    normalizedIds,
                    ownerUserId);
                if (rejectionReason != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ChatMediaReservationResult.Rejected(rejectionReason);
                }

                if (assets.All(IsPendingAndUnassigned))
                {
                    foreach (var asset in assets)
                    {
                        asset.Status = MediaAssetStatus.Reserved;
                        asset.ReservedByMessageId = messageId;
                        asset.ReservedAt = reservedAtUtc;
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return ChatMediaReservationResult.Success(true);
                }

                if (assets.All(asset => IsReservedByMessage(asset, messageId)))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ChatMediaReservationResult.Success(false);
                }

                if (assets.All(asset => IsAttachedToMessage(asset, roomId, messageId)))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ChatMediaReservationResult.Success(false);
                }

                await transaction.RollbackAsync(cancellationToken);
                return ChatMediaReservationResult.Rejected(
                    "Mot hoac nhieu attachment da duoc giu/gan boi tin nhan khac.");
            }
            catch (Exception ex) when (
                attempt < MaxTransactionAttempts &&
                IsSerializationFailure(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning(
                    ex,
                    "Xung dot transaction khi reserve attachment cho MessageId={MessageId}; thu lai lan {Attempt}.",
                    messageId,
                    attempt + 1);
            }
        }

        throw new InvalidOperationException(
            $"Khong the reserve attachment cho message {messageId} sau {MaxTransactionAttempts} lan.");
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        IReadOnlyCollection<Guid> mediaIds,
        string messageId,
        CancellationToken cancellationToken)
    {
        var normalizedIds = NormalizeMediaIds(mediaIds);
        if (normalizedIds.Count == 0)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        await _dbContext.MediaAssets
            .Where(asset =>
                normalizedIds.Contains(asset.Id) &&
                asset.Status == MediaAssetStatus.Reserved &&
                asset.ReservedByMessageId == messageId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(asset => asset.Status, MediaAssetStatus.Pending)
                    .SetProperty(asset => asset.ReservedByMessageId, (string?)null)
                    .SetProperty(asset => asset.ReservedAt, (DateTime?)null),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaAsset>?> LoadForPersistenceAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken)
    {
        var normalizedIds = NormalizeMediaIds(mediaIds);
        if (normalizedIds.Count == 0)
            return [];

        ValidateMessageContext(ownerUserId, roomId, messageId);

        var assets = await _dbContext.MediaAssets
            .AsNoTracking()
            .Where(asset => normalizedIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);

        if (ValidateCommonState(assets, normalizedIds, ownerUserId) != null)
            return null;

        var assetsById = assets.ToDictionary(asset => asset.Id);
        var orderedAssets = new List<MediaAsset>(normalizedIds.Count);

        foreach (var mediaId in normalizedIds)
        {
            var asset = assetsById[mediaId];
            if (!IsReservedByMessage(asset, messageId) &&
                !IsAttachedToMessage(asset, roomId, messageId))
            {
                return null;
            }

            orderedAssets.Add(asset);
        }

        return orderedAssets;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        DateTime attachedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await CompleteWithResultAsync(
            mediaIds,
            ownerUserId,
            roomId,
            messageId,
            attachedAtUtc,
            cancellationToken);

        return result.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<ChatMediaCompletionResult> CompleteWithResultAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        DateTime attachedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedIds = NormalizeMediaIds(mediaIds);
        if (normalizedIds.Count == 0)
            return ChatMediaCompletionResult.Completed(isNoOp: true);

        ValidateMessageContext(ownerUserId, roomId, messageId);

        for (var attempt = 1; attempt <= MaxTransactionAttempts; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var assets = await _dbContext.MediaAssets
                    .Where(asset => normalizedIds.Contains(asset.Id))
                    .ToListAsync(cancellationToken);

                var commonStateError = ValidateCommonState(assets, normalizedIds, ownerUserId);
                if (commonStateError != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ChatMediaCompletionResult.Conflict(
                        "media_completion_invalid_assets",
                        commonStateError);
                }

                var conflictingAsset = assets.FirstOrDefault(asset =>
                    !IsReservedByMessage(asset, messageId) &&
                    !IsAttachedToMessage(asset, roomId, messageId));
                if (conflictingAsset != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ChatMediaCompletionResult.Conflict(
                        "media_completion_state_conflict",
                        $"Attachment {conflictingAsset.Id} khong thuoc reservation/message can hoan tat.");
                }

                var reservedAssets = assets
                    .Where(asset => asset.Status == MediaAssetStatus.Reserved)
                    .ToList();
                if (reservedAssets.Count == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ChatMediaCompletionResult.Completed(isNoOp: true);
                }

                foreach (var asset in reservedAssets)
                {
                    asset.Status = MediaAssetStatus.Attached;
                    asset.RoomId = roomId;
                    asset.MessageId = messageId;
                    asset.AttachedAt ??= attachedAtUtc;
                    asset.ReservedByMessageId = null;
                    asset.ReservedAt = null;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ChatMediaCompletionResult.Completed(isNoOp: false);
            }
            catch (Exception ex) when (
                attempt < MaxTransactionAttempts &&
                IsSerializationFailure(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning(
                    ex,
                    "Xung dot transaction khi complete attachment cho MessageId={MessageId}; thu lai lan {Attempt}.",
                    messageId,
                    attempt + 1);
            }
        }

        throw new InvalidOperationException(
            $"Khong the complete attachment cho message {messageId} sau {MaxTransactionAttempts} lan.");
    }

    private static List<Guid> NormalizeMediaIds(IReadOnlyCollection<Guid> mediaIds)
    {
        return (mediaIds ?? [])
            .Where(mediaId => mediaId != Guid.Empty)
            .Distinct()
            .ToList();
    }

    private static void ValidateMessageContext(
        Guid ownerUserId,
        Guid roomId,
        string messageId)
    {
        if (ownerUserId == Guid.Empty)
            throw new ArgumentException("OwnerUserId khong hop le.", nameof(ownerUserId));

        if (roomId == Guid.Empty)
            throw new ArgumentException("RoomId khong hop le.", nameof(roomId));

        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (messageId.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(messageId), "MessageId vuot qua 64 ky tu.");
    }

    private static string? ValidateCommonState(
        IReadOnlyCollection<MediaAsset> assets,
        IReadOnlyCollection<Guid> expectedIds,
        Guid ownerUserId)
    {
        if (assets.Count != expectedIds.Count)
            return "Khong tim thay day du attachment.";

        var invalidAsset = assets.FirstOrDefault(asset =>
            asset.OwnerUserId != ownerUserId ||
            asset.Scope != MediaScope.ChatAttachment ||
            asset.DeletedAt != null);

        return invalidAsset == null
            ? null
            : $"Attachment {invalidAsset.Id} khong thuoc nguoi gui, sai scope hoac da bi xoa.";
    }

    private static bool IsPendingAndUnassigned(MediaAsset asset)
    {
        return asset.Status == MediaAssetStatus.Pending &&
               asset.RoomId == null &&
               asset.MessageId == null &&
               asset.ReservedByMessageId == null &&
               asset.ReservedAt == null;
    }

    private static bool IsReservedByMessage(MediaAsset asset, string messageId)
    {
        return asset.Status == MediaAssetStatus.Reserved &&
               asset.RoomId == null &&
               asset.MessageId == null &&
               asset.ReservedByMessageId == messageId &&
               asset.ReservedAt.HasValue;
    }

    private static bool IsAttachedToMessage(
        MediaAsset asset,
        Guid roomId,
        string messageId)
    {
        return asset.Status == MediaAssetStatus.Attached &&
               asset.RoomId == roomId &&
               asset.MessageId == messageId &&
               asset.ReservedByMessageId == null &&
               asset.ReservedAt == null;
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        if (exception is PostgresException postgresException)
            return postgresException.SqlState == PostgresErrorCodes.SerializationFailure;

        return exception.InnerException != null &&
               IsSerializationFailure(exception.InnerException);
    }
}
