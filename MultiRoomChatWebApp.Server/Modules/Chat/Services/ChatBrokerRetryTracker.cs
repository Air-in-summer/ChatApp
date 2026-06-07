using System.Globalization;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class ChatBrokerRetryTracker
{
    private const int MaxStoredErrorLength = 1_024;

    private const string RecordFailureScript = """
local attempt = redis.call('HINCRBY', KEYS[1], 'attempt', 1)
local exponent = math.min(attempt - 1, 30)
local delay = math.min(tonumber(ARGV[3]), tonumber(ARGV[2]) * (2 ^ exponent))
local jittered_delay = math.max(1, math.floor(delay * tonumber(ARGV[4]) / 1000))
local next_retry_at = tonumber(ARGV[1]) + jittered_delay

redis.call('HSETNX', KEYS[1], 'firstFailedAtUnixMs', ARGV[1])
redis.call(
    'HSET',
    KEYS[1],
    'nextRetryAtUnixMs', next_retry_at,
    'failureKind', ARGV[5],
    'code', ARGV[6],
    'lastError', ARGV[7],
    'updatedAtUnixMs', ARGV[1])
redis.call('PEXPIRE', KEYS[1], ARGV[8])

return { attempt, next_retry_at }
""";

    private static readonly RedisValue[] StateFields =
    [
        "attempt",
        "nextRetryAtUnixMs",
        "failureKind",
        "code",
        "lastError"
    ];

    private readonly IDatabase _database;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly ChatBrokerConsumerKind _consumerKind;
    private readonly ChatBrokerOptions _options;

    public ChatBrokerRetryTracker(
        IDatabase database,
        IChatBrokerKeyProvider keyProvider,
        ChatBrokerConsumerKind consumerKind,
        ChatBrokerOptions options)
    {
        _database = database;
        _keyProvider = keyProvider;
        _consumerKind = consumerKind;
        _options = options;
    }

    public async Task<ChatBrokerRetryDecision> GetDecisionAsync(
        RedisValue entryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var values = await _database.HashGetAsync(
            _keyProvider.BuildRetryStateKey(_consumerKind, entryId),
            StateFields);

        cancellationToken.ThrowIfCancellationRequested();

        if (values.Length != StateFields.Length ||
            values[0].IsNullOrEmpty ||
            !int.TryParse(
                values[0].ToString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var attempt))
        {
            return new ChatBrokerRetryDecision(
                0,
                null,
                IsDue: true,
                IsExhausted: false,
                FailureKind: null,
                ErrorCode: null,
                LastError: null);
        }

        var nextRetryAtUnixMs = 0L;
        var hasNextRetryAt = !values[1].IsNullOrEmpty &&
                             long.TryParse(
                                 values[1].ToString(),
                                 NumberStyles.None,
                                 CultureInfo.InvariantCulture,
                                 out nextRetryAtUnixMs);
        var nextRetryAtUtc = hasNextRetryAt
            ? DateTimeOffset.FromUnixTimeMilliseconds(nextRetryAtUnixMs).UtcDateTime
            : DateTime.MinValue;
        var isExhausted = attempt >= _options.MaxAttempts;
        ChatBrokerEntryFailureKind? failureKind = Enum.TryParse<ChatBrokerEntryFailureKind>(
            values[2].ToString(),
            ignoreCase: true,
            out var parsedFailureKind)
            ? parsedFailureKind
            : null;

        return new ChatBrokerRetryDecision(
            attempt,
            hasNextRetryAt ? nextRetryAtUtc : null,
            IsDue: !isExhausted && (!hasNextRetryAt || nextRetryAtUtc <= DateTime.UtcNow),
            IsExhausted: isExhausted,
            FailureKind: failureKind,
            ErrorCode: values[3].IsNullOrEmpty ? null : values[3].ToString(),
            LastError: values[4].IsNullOrEmpty ? null : values[4].ToString());
    }

    public async Task<ChatBrokerRetryState> RecordFailureAsync(
        RedisValue entryId,
        ChatBrokerEntryFailureKind failureKind,
        string code,
        string error,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var jitterPermille = Random.Shared.Next(500, 1_001);
        var ttlMs = (long)Math.Ceiling(_options.RetryStateTtl.TotalMilliseconds);
        var rawResult = await _database.ScriptEvaluateAsync(
            RecordFailureScript,
            [_keyProvider.BuildRetryStateKey(_consumerKind, entryId)],
            [
                nowUnixMs,
                (long)Math.Ceiling(_options.RetryBaseDelay.TotalMilliseconds),
                (long)Math.Ceiling(_options.RetryMaxDelay.TotalMilliseconds),
                jitterPermille,
                failureKind.ToString(),
                code,
                Truncate(error, MaxStoredErrorLength),
                ttlMs
            ]);

        cancellationToken.ThrowIfCancellationRequested();

        var values = (RedisResult[]?)rawResult
            ?? throw new InvalidOperationException("Ket qua ghi retry state rong.");
        if (values.Length != 2)
        {
            throw new InvalidOperationException(
                $"Ket qua ghi retry state khong hop le. Length={values.Length}.");
        }

        var attempt = int.Parse(values[0].ToString(), CultureInfo.InvariantCulture);
        var nextRetryAtUnixMs = long.Parse(values[1].ToString(), CultureInfo.InvariantCulture);

        return new ChatBrokerRetryState(
            attempt,
            DateTimeOffset.FromUnixTimeMilliseconds(nextRetryAtUnixMs).UtcDateTime);
    }

    public async Task ClearAsync(
        RedisValue entryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.KeyDeleteAsync(
            _keyProvider.BuildRetryStateKey(_consumerKind, entryId));
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
