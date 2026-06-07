using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class ChatBrokerMaintenanceWorker : BackgroundService
{
    private const int MarkerDeleteBatchSize = 1_000;

    private readonly IChatBrokerConnection _brokerConnection;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly ChatBrokerOptions _options;
    private readonly ILogger<ChatBrokerMaintenanceWorker> _logger;

    public ChatBrokerMaintenanceWorker(
        IChatBrokerConnection brokerConnection,
        IChatBrokerKeyProvider keyProvider,
        IOptions<ChatBrokerOptions> options,
        ILogger<ChatBrokerMaintenanceWorker> logger)
    {
        _brokerConnection = brokerConnection;
        _keyProvider = keyProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(_options.MaintenanceInterval, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat broker maintenance that bai.");
            }

            await Task.Delay(_options.MaintenanceInterval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var database = _brokerConnection.GetDatabase();
        var lockValue = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var acquired = await database.LockTakeAsync(
            _keyProvider.MaintenanceLockKey,
            lockValue,
            _options.MaintenanceLockTtl);
        if (!acquired)
        {
            _logger.LogDebug("Bo qua chat broker maintenance vi instance khac dang giu lock.");
            return;
        }

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var renewalCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = RenewLockAsync(
            database,
            lockValue,
            renewalCancellation.Token,
            operationCancellation);

        try
        {
            var startedAtUtc = DateTime.UtcNow;
            var operationToken = operationCancellation.Token;
            var deadLetterCutoff = ToStreamId(startedAtUtc - _options.DeadLetterRetention);
            var persistenceDlqTrimmed = await database.StreamTrimByMinIdAsync(
                _keyProvider.PersistenceDeadLetterStream,
                deadLetterCutoff,
                useApproximateMaxLength: false,
                limit: null,
                mode: StreamTrimMode.KeepReferences);
            operationToken.ThrowIfCancellationRequested();
            var deliveryDlqTrimmed = await database.StreamTrimByMinIdAsync(
                _keyProvider.DeliveryDeadLetterStream,
                deadLetterCutoff,
                useApproximateMaxLength: false,
                limit: null,
                mode: StreamTrimMode.KeepReferences);
            operationToken.ThrowIfCancellationRequested();
            var sourceTrimBoundary = await CalculateSafeSourceTrimBoundaryAsync(
                database,
                startedAtUtc,
                operationToken);
            long sourceTrimmed = 0;
            long sendMarkersDeleted = 0;
            if (sourceTrimBoundary.HasValue &&
                sourceTrimBoundary.Value > StreamId.Zero)
            {
                sendMarkersDeleted = await DeleteSendMarkersBeforeBoundaryAsync(
                    database,
                    sourceTrimBoundary.Value,
                    operationToken);
                operationToken.ThrowIfCancellationRequested();

                sourceTrimmed = await database.StreamTrimByMinIdAsync(
                    _keyProvider.MessageStream,
                    sourceTrimBoundary.Value.ToString(),
                    useApproximateMaxLength: false,
                    limit: null,
                    mode: StreamTrimMode.KeepReferences);
                operationToken.ThrowIfCancellationRequested();
            }

            var completedAtUtc = DateTime.UtcNow;
            await database.HashSetAsync(
                _keyProvider.MaintenanceStateKey,
                [
                    new HashEntry("lastStartedAtUtc", startedAtUtc.ToString("O")),
                    new HashEntry("lastCompletedAtUtc", completedAtUtc.ToString("O")),
                    new HashEntry(
                        "sourceTrimBoundary",
                        sourceTrimBoundary?.ToString() ?? string.Empty),
                    new HashEntry("sourceTrimmedCount", sourceTrimmed),
                    new HashEntry("sendMarkersDeletedCount", sendMarkersDeleted),
                    new HashEntry(
                        "persistenceDlqTrimmedCount",
                        persistenceDlqTrimmed),
                    new HashEntry(
                        "deliveryDlqTrimmedCount",
                        deliveryDlqTrimmed)
                ]);

            _logger.LogInformation(
                "Chat broker maintenance hoan tat. SourceBoundary={SourceBoundary}; SourceTrimmed={SourceTrimmed}; SendMarkersDeleted={SendMarkersDeleted}; PersistenceDlqTrimmed={PersistenceDlqTrimmed}; DeliveryDlqTrimmed={DeliveryDlqTrimmed}",
                sourceTrimBoundary?.ToString(),
                sourceTrimmed,
                sendMarkersDeleted,
                persistenceDlqTrimmed,
                deliveryDlqTrimmed);
        }
        finally
        {
            renewalCancellation.Cancel();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when this maintenance run completes.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Tac vu gia han maintenance lock ket thuc voi loi.");
            }

            await database.LockReleaseAsync(
                _keyProvider.MaintenanceLockKey,
                lockValue);
        }
    }

    private async Task RenewLockAsync(
        IDatabase database,
        RedisValue lockValue,
        CancellationToken cancellationToken,
        CancellationTokenSource operationCancellation)
    {
        var renewalInterval = TimeSpan.FromMilliseconds(
            Math.Max(
                1_000,
                _options.MaintenanceLockTtl.TotalMilliseconds / 3));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(renewalInterval, cancellationToken);
                var extended = await database.LockExtendAsync(
                    _keyProvider.MaintenanceLockKey,
                    lockValue,
                    _options.MaintenanceLockTtl);
                if (extended)
                {
                    continue;
                }

                _logger.LogError(
                    "Chat broker maintenance mat distributed lock; huy cac buoc tiep theo.");
                operationCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Khong gia han duoc chat broker maintenance lock; huy cac buoc tiep theo.");
            operationCancellation.Cancel();
        }
    }

    private async Task<StreamId?> CalculateSafeSourceTrimBoundaryAsync(
        IDatabase database,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var groupInfos = await database.StreamGroupInfoAsync(
            _keyProvider.MessageStream);
        var persistenceInfo = groupInfos.FirstOrDefault(
            group => group.Name == _keyProvider.PersistenceGroup);
        var deliveryInfo = groupInfos.FirstOrDefault(
            group => group.Name == _keyProvider.DeliveryGroup);
        if (string.IsNullOrEmpty(persistenceInfo.Name) ||
            string.IsNullOrEmpty(deliveryInfo.Name))
        {
            _logger.LogWarning(
                "Khong trim source stream vi thieu consumer group. PersistenceFound={PersistenceFound}; DeliveryFound={DeliveryFound}",
                !string.IsNullOrEmpty(persistenceInfo.Name),
                !string.IsNullOrEmpty(deliveryInfo.Name));
            return null;
        }

        var graceBoundary = StreamId.Parse(
            ToStreamId(nowUtc - _options.ProcessedStreamGrace));
        var persistenceBoundary = await CalculateGroupBoundaryAsync(
            database,
            persistenceInfo,
            _keyProvider.PersistenceGroup,
            cancellationToken);
        var deliveryBoundary = await CalculateGroupBoundaryAsync(
            database,
            deliveryInfo,
            _keyProvider.DeliveryGroup,
            cancellationToken);
        if (!persistenceBoundary.HasValue || !deliveryBoundary.HasValue)
        {
            return null;
        }

        return StreamId.Min(
            graceBoundary,
            StreamId.Min(
                persistenceBoundary.Value,
                deliveryBoundary.Value));
    }

    private async Task<long> DeleteSendMarkersBeforeBoundaryAsync(
        IDatabase database,
        StreamId boundary,
        CancellationToken cancellationToken)
    {
        var deleted = 0L;
        var minId = "-";

        while (!cancellationToken.IsCancellationRequested)
        {
            var entries = await database.StreamRangeAsync(
                _keyProvider.MessageStream,
                minId,
                boundary.ToString(),
                count: MarkerDeleteBatchSize,
                messageOrder: Order.Ascending);
            if (entries.Length == 0)
            {
                return deleted;
            }

            var markerKeys = new List<RedisKey>();
            RedisValue? lastEntryId = null;
            foreach (var entry in entries)
            {
                lastEntryId = entry.Id;
                if (!StreamId.TryParse(entry.Id, out var entryStreamId) ||
                    entryStreamId.CompareTo(boundary) >= 0)
                {
                    continue;
                }

                var payload = entry.Values
                    .FirstOrDefault(value => value.Name == "payload")
                    .Value
                    .ToString();
                if (TryReadAcceptedEvent(payload, out var acceptedEvent))
                {
                    markerKeys.Add(_keyProvider.BuildSendMarkerKey(
                        acceptedEvent.SenderId,
                        acceptedEvent.ClientMessageId));
                }
            }

            if (markerKeys.Count > 0)
            {
                deleted += await database.KeyDeleteAsync(markerKeys.Distinct().ToArray());
            }

            if (entries.Length < MarkerDeleteBatchSize || !lastEntryId.HasValue)
            {
                return deleted;
            }

            minId = NextStreamId(lastEntryId.Value);
        }

        return deleted;
    }

    private static bool TryReadAcceptedEvent(
        string payload,
        out MessageAcceptedEventV1 acceptedEvent)
    {
        acceptedEvent = null!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var candidate = JsonSerializer.Deserialize<MessageAcceptedEventV1>(payload);
            if (candidate is null ||
                candidate.SenderId == Guid.Empty ||
                candidate.ClientMessageId == Guid.Empty)
            {
                return false;
            }

            acceptedEvent = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<StreamId?> CalculateGroupBoundaryAsync(
        IDatabase database,
        StreamGroupInfo groupInfo,
        RedisValue groupName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!StreamId.TryParse(groupInfo.LastDeliveredId, out var lastDelivered) ||
            lastDelivered == StreamId.Zero)
        {
            return null;
        }

        if (groupInfo.PendingMessageCount <= 0)
        {
            return lastDelivered;
        }

        var pendingInfo = await database.StreamPendingAsync(
            _keyProvider.MessageStream,
            groupName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!StreamId.TryParse(
                pendingInfo.LowestPendingMessageId,
                out var lowestPending))
        {
            _logger.LogWarning(
                "Khong trim source stream vi group co pending nhung khong doc duoc lowest pending id. Group={Group}",
                groupName);
            return null;
        }

        return StreamId.Min(lastDelivered, lowestPending);
    }

    private static string ToStreamId(DateTime utc)
    {
        var unixMs = new DateTimeOffset(utc.ToUniversalTime())
            .ToUnixTimeMilliseconds();
        return $"{unixMs.ToString(CultureInfo.InvariantCulture)}-0";
    }

    private static string NextStreamId(RedisValue entryId)
    {
        return StreamId.TryParse(entryId, out var parsed)
            ? new StreamId(parsed.Milliseconds, parsed.Sequence + 1).ToString()
            : entryId.ToString();
    }

    private readonly record struct StreamId(long Milliseconds, long Sequence)
        : IComparable<StreamId>
    {
        public static StreamId Zero => new(0, 0);

        public int CompareTo(StreamId other)
        {
            var millisecondsComparison = Milliseconds.CompareTo(other.Milliseconds);
            return millisecondsComparison != 0
                ? millisecondsComparison
                : Sequence.CompareTo(other.Sequence);
        }

        public static bool operator >(StreamId left, StreamId right)
            => left.CompareTo(right) > 0;

        public static bool operator <(StreamId left, StreamId right)
            => left.CompareTo(right) < 0;

        public static StreamId Min(StreamId left, StreamId right)
            => left < right ? left : right;

        public static StreamId Parse(RedisValue value)
            => TryParse(value, out var result)
                ? result
                : throw new FormatException($"Redis stream id khong hop le: {value}");

        public static bool TryParse(RedisValue value, out StreamId result)
        {
            result = Zero;
            var parts = value.ToString().Split('-', 2);
            if (parts.Length != 2 ||
                !long.TryParse(
                    parts[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var milliseconds) ||
                !long.TryParse(
                    parts[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var sequence))
            {
                return false;
            }

            result = new StreamId(milliseconds, sequence);
            return true;
        }

        public override string ToString()
            => $"{Milliseconds.ToString(CultureInfo.InvariantCulture)}-{Sequence.ToString(CultureInfo.InvariantCulture)}";
    }
}
