using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class ChatBrokerPendingEntryReclaimer
{
    private static readonly RedisValue InitialCursor = "0-0";

    private readonly RedisKey _stream;
    private readonly RedisValue _consumerGroup;
    private readonly RedisValue _consumerName;
    private readonly long _minimumIdleTimeMs;
    private readonly int _batchSize;
    private readonly TimeSpan _scanInterval;

    private RedisValue _cursor = InitialCursor;
    private DateTime _nextScanAtUtc = DateTime.MinValue;

    public ChatBrokerPendingEntryReclaimer(
        RedisKey stream,
        RedisValue consumerGroup,
        RedisValue consumerName,
        TimeSpan visibilityTimeout,
        int batchSize)
    {
        if (visibilityTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(visibilityTimeout),
                "Visibility timeout phai lon hon 0.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Reclaim batch size phai lon hon 0.");
        }

        _stream = stream;
        _consumerGroup = consumerGroup;
        _consumerName = consumerName;
        _minimumIdleTimeMs = (long)Math.Ceiling(visibilityTimeout.TotalMilliseconds);
        _batchSize = batchSize;
        _scanInterval = TimeSpan.FromMilliseconds(
            Math.Clamp(
                visibilityTimeout.TotalMilliseconds / 2,
                TimeSpan.FromSeconds(1).TotalMilliseconds,
                TimeSpan.FromSeconds(30).TotalMilliseconds));
    }

    public async Task<StreamAutoClaimResult?> ClaimDueAsync(
        IDatabase database,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc < _nextScanAtUtc)
        {
            return null;
        }

        _nextScanAtUtc = nowUtc.Add(_scanInterval);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await database.StreamAutoClaimAsync(
            _stream,
            _consumerGroup,
            _consumerName,
            _minimumIdleTimeMs,
            _cursor,
            _batchSize);

        cancellationToken.ThrowIfCancellationRequested();

        _cursor = result.NextStartId.IsNullOrEmpty
            ? InitialCursor
            : result.NextStartId;

        return result;
    }
}
