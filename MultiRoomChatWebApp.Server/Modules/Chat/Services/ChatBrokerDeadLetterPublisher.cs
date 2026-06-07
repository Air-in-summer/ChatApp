using System.Globalization;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class ChatBrokerDeadLetterPublisher
{
    private const string PublishAndAcknowledgeScript = """
local attempt = tonumber(redis.call('HGET', KEYS[3], 'attempt') or '0')
if attempt < tonumber(ARGV[1]) then
    return { 'not_exhausted', '', tostring(attempt), '0' }
end

local existing_dlq_id = redis.call('HGET', KEYS[3], 'dlqStreamId')
if existing_dlq_id then
    local acknowledged = redis.call('XACK', KEYS[1], ARGV[2], ARGV[3])
    return { 'existing', existing_dlq_id, tostring(attempt), tostring(acknowledged) }
end

local failure_kind = redis.call('HGET', KEYS[3], 'failureKind') or 'Transient'
local code = redis.call('HGET', KEYS[3], 'code') or 'unknown'
local last_error = redis.call('HGET', KEYS[3], 'lastError') or ''
local first_failed_at = redis.call('HGET', KEYS[3], 'firstFailedAtUnixMs') or ARGV[7]
local last_failed_at = redis.call('HGET', KEYS[3], 'updatedAtUnixMs') or ARGV[7]

local dlq_id = redis.call(
    'XADD',
    KEYS[2],
    '*',
    'schemaVersion', '1',
    'workerKind', ARGV[4],
    'sourceStream', ARGV[5],
    'sourceEntryId', ARGV[3],
    'consumerGroup', ARGV[2],
    'consumerName', ARGV[6],
    'originalPayload', ARGV[8],
    'originalFields', ARGV[9],
    'attempt', tostring(attempt),
    'maxAttempts', ARGV[1],
    'failureKind', failure_kind,
    'code', code,
    'lastError', last_error,
    'firstFailedAtUnixMs', first_failed_at,
    'lastFailedAtUnixMs', last_failed_at,
    'deadLetteredAtUnixMs', ARGV[7])

redis.call('HSET', KEYS[3], 'dlqStreamId', dlq_id)
local acknowledged = redis.call('XACK', KEYS[1], ARGV[2], ARGV[3])
if acknowledged > 0 then
    redis.call('DEL', KEYS[3])
end

return { 'created', dlq_id, tostring(attempt), tostring(acknowledged) }
""";

    private readonly IDatabase _database;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly ChatBrokerOptions _options;

    public ChatBrokerDeadLetterPublisher(
        IDatabase database,
        IChatBrokerKeyProvider keyProvider,
        ChatBrokerOptions options)
    {
        _database = database;
        _keyProvider = keyProvider;
        _options = options;
    }

    public async Task<ChatBrokerDeadLetterResult> PublishPersistenceAsync(
        RedisValue entryId,
        RedisValue consumerName,
        string originalPayload,
        string originalFields,
        CancellationToken cancellationToken)
    {
        return await PublishAsync(
            ChatBrokerConsumerKind.Persistence,
            _keyProvider.PersistenceGroup,
            _keyProvider.PersistenceDeadLetterStream,
            "persistence",
            entryId,
            consumerName,
            originalPayload,
            originalFields,
            cancellationToken);
    }

    public async Task<ChatBrokerDeadLetterResult> PublishDeliveryAsync(
        RedisValue entryId,
        RedisValue consumerName,
        string originalPayload,
        string originalFields,
        CancellationToken cancellationToken)
    {
        return await PublishAsync(
            ChatBrokerConsumerKind.Delivery,
            _keyProvider.DeliveryGroup,
            _keyProvider.DeliveryDeadLetterStream,
            "delivery",
            entryId,
            consumerName,
            originalPayload,
            originalFields,
            cancellationToken);
    }

    private async Task<ChatBrokerDeadLetterResult> PublishAsync(
        ChatBrokerConsumerKind consumerKind,
        RedisValue consumerGroup,
        RedisKey deadLetterStream,
        string workerKind,
        RedisValue entryId,
        RedisValue consumerName,
        string originalPayload,
        string originalFields,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rawResult = await _database.ScriptEvaluateAsync(
            PublishAndAcknowledgeScript,
            [
                _keyProvider.MessageStream,
                deadLetterStream,
                _keyProvider.BuildRetryStateKey(
                    consumerKind,
                    entryId)
            ],
            [
                _options.MaxAttempts,
                consumerGroup,
                entryId,
                workerKind,
                _keyProvider.MessageStream.ToString(),
                consumerName,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                originalPayload,
                originalFields
            ]);

        cancellationToken.ThrowIfCancellationRequested();

        var values = (RedisResult[]?)rawResult
            ?? throw new InvalidOperationException(
                $"Ket qua publish {workerKind} DLQ rong.");
        if (values.Length != 4)
        {
            throw new InvalidOperationException(
                $"Ket qua publish {workerKind} DLQ khong hop le. Length={values.Length}.");
        }

        var status = values[0].ToString();
        var deadLetterEntryId = values[1].ToString();
        var attempt = int.Parse(values[2].ToString(), CultureInfo.InvariantCulture);
        var acknowledgedCount = long.Parse(
            values[3].ToString(),
            CultureInfo.InvariantCulture);

        if (string.Equals(status, "not_exhausted", StringComparison.Ordinal))
        {
            return new ChatBrokerDeadLetterResult(
                IsDeadLettered: false,
                DeadLetterEntryId: null,
                attempt,
                acknowledgedCount);
        }

        if (!string.Equals(status, "created", StringComparison.Ordinal) &&
            !string.Equals(status, "existing", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Trang thai publish {workerKind} DLQ khong hop le: {status}.");
        }

        return new ChatBrokerDeadLetterResult(
            IsDeadLettered: true,
            DeadLetterEntryId: deadLetterEntryId,
            attempt,
            acknowledgedCount);
    }
}
