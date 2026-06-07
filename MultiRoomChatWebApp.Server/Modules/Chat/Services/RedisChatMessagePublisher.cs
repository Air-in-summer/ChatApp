using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class RedisChatMessagePublisher : IChatMessagePublisher
{
    private const string PayloadFieldName = "payload";

    private const string PublishScript = """
local existing_stream_id = redis.call('HGET', KEYS[2], 'streamId')
if existing_stream_id then
    return {
        'duplicate',
        existing_stream_id,
        redis.call('HGET', KEYS[2], 'messageId'),
        redis.call('HGET', KEYS[2], 'acceptedAtUtc')
    }
end

local stream_id = redis.call('XADD', KEYS[1], '*', ARGV[1], ARGV[2])
redis.call(
    'HSET',
    KEYS[2],
    'streamId', stream_id,
    'messageId', ARGV[3],
    'acceptedAtUtc', ARGV[4])
redis.call('PEXPIRE', KEYS[2], ARGV[5])

return {
    'created',
    stream_id,
    ARGV[3],
    ARGV[4]
}
""";

    private readonly IChatBrokerConnection _brokerConnection;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly ChatBrokerOptions _options;
    private readonly ILogger<RedisChatMessagePublisher> _logger;

    public RedisChatMessagePublisher(
        IChatBrokerConnection brokerConnection,
        IChatBrokerKeyProvider keyProvider,
        IOptions<ChatBrokerOptions> options,
        ILogger<RedisChatMessagePublisher> logger)
    {
        _brokerConnection = brokerConnection;
        _keyProvider = keyProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatMessagePublishResult> PublishAsync(
        MessageAcceptedEventV1 acceptedEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEvent(acceptedEvent);

        var payload = JsonSerializer.Serialize(acceptedEvent);
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);
        if (payloadBytes > MessageAcceptedEventV1.MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Message event payload vuot qua {MessageAcceptedEventV1.MaxPayloadBytes} bytes.");
        }

        var sendMarkerKey = _keyProvider.BuildSendMarkerKey(
            acceptedEvent.SenderId,
            acceptedEvent.ClientMessageId);
        var acceptedAtUtc = acceptedEvent.AcceptedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        var idempotencyTtlMs = ((long)Math.Ceiling(_options.IdempotencyTtl.TotalMilliseconds))
            .ToString(CultureInfo.InvariantCulture);

        var database = _brokerConnection.GetDatabase();
        var rawResult = await database.ScriptEvaluateAsync(
            PublishScript,
            [_keyProvider.MessageStream, sendMarkerKey],
            [
                PayloadFieldName,
                payload,
                acceptedEvent.MessageId,
                acceptedAtUtc,
                idempotencyTtlMs
            ]);

        cancellationToken.ThrowIfCancellationRequested();

        var values = (RedisResult[]?)rawResult
            ?? throw new InvalidOperationException("Ket qua publish message rong.");
        if (values.Length != 4)
        {
            throw new InvalidOperationException(
                $"Ket qua publish message khong hop le. Length={values.Length}.");
        }

        var status = values[0].ToString();
        var isNewEvent = string.Equals(status, "created", StringComparison.Ordinal);
        if (!isNewEvent && !string.Equals(status, "duplicate", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Trang thai publish message khong hop le: {status}.");
        }

        var result = new ChatMessagePublishResult(
            isNewEvent,
            ReadRequired(values, 1),
            ReadRequired(values, 2),
            DateTime.Parse(
                ReadRequired(values, 3),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));

        if (!isNewEvent)
        {
            _logger.LogInformation(
                "Message publish duplicate ignored. MessageId={MessageId}, StreamId={StreamId}",
                result.MessageId,
                result.StreamId);
        }

        return result;
    }

    private static void ValidateEvent(MessageAcceptedEventV1 acceptedEvent)
    {
        if (acceptedEvent.SchemaVersion != MessageAcceptedEventV1.CurrentSchemaVersion)
        {
            throw new ArgumentException("SchemaVersion cua message event khong hop le.", nameof(acceptedEvent));
        }

        if (acceptedEvent.CorrelationId == Guid.Empty ||
            acceptedEvent.ClientMessageId == Guid.Empty ||
            acceptedEvent.RoomId == Guid.Empty ||
            acceptedEvent.SenderId == Guid.Empty ||
            acceptedEvent.AcceptedAtUtc == default ||
            !ObjectId.TryParse(acceptedEvent.MessageId, out _))
        {
            throw new ArgumentException("Message event thieu dinh danh bat buoc.", nameof(acceptedEvent));
        }

        var content = acceptedEvent.Content ?? string.Empty;
        if (content.Length > MessageAcceptedEventV1.MaxContentLength)
        {
            throw new ArgumentException("Noi dung message event vuot gioi han.", nameof(acceptedEvent));
        }

        var mediaIds = acceptedEvent.MediaIds ?? [];
        var attachments = acceptedEvent.Attachments ?? [];
        if (mediaIds.Count > MessageAcceptedEventV1.MaxAttachmentCount ||
            attachments.Count != mediaIds.Count)
        {
            throw new ArgumentException("Attachment cua message event khong hop le.", nameof(acceptedEvent));
        }
    }

    private static string ReadRequired(IReadOnlyList<RedisResult> values, int index)
    {
        var value = values[index].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Ket qua publish message thieu gia tri tai index {index}.");
        }

        return value;
    }
}
