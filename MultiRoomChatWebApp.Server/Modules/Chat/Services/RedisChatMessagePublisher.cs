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

/// <inheritdoc />
public sealed class RedisChatMessagePublisher : IChatMessagePublisher
{
    private const string PayloadFieldName = "payload";

    // Redis cần làm 2 việc như một giao dịch nhỏ:
    // 1. Nếu lần gửi này đã có marker thì trả lại kết quả cũ, không ghi thêm tin mới.
    // 2. Nếu chưa có marker thì XADD event vào stream, lưu marker, rồi trả kết quả mới.
    // Lua script bảo đảm hai bước đó chạy liền mạch trong Redis, tránh race condition khi retry.
    //
    // Tham số truyền vào script:
    // KEYS[1] = Redis Stream chứa event tin nhắn, ví dụ chat:{messages}:v1.
    // KEYS[2] = marker chống trùng cho senderId + clientMessageId.
    // ARGV[1] = tên field trong stream entry, hiện là "payload".
    // ARGV[2] = JSON của MessageAcceptedEventV1.
    // ARGV[3] = messageId server đã cấp.
    // ARGV[4] = acceptedAtUtc.
    // ARGV[5] = TTL của marker theo milliseconds.
    //
    // Luồng trong script:
    // 1. Đọc marker; nếu đã có streamId thì trả "duplicate" cùng dữ liệu cũ.
    // 2. Nếu chưa có marker thì XADD payload vào stream.
    // 3. Lưu streamId/messageId/acceptedAtUtc vào marker và đặt TTL.
    // 4. Trả "created" cùng streamId/messageId/acceptedAtUtc mới.
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

    /// <inheritdoc />
    public async Task<ChatMessagePublishResult> PublishAsync(
        MessageAcceptedEventV1 acceptedEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Bước 1: kiểm tra phiếu gửi tin trước khi đưa vào hàng đợi xử lý.
        // Phiếu này phải có đủ room, người gửi, mã tin và thời điểm server chấp nhận.
        ValidateEvent(acceptedEvent);

        // Bước 2: đóng gói toàn bộ thông tin tin nhắn thành một chuỗi JSON.
        // Hai worker phía sau sẽ đọc lại đúng gói này: một worker để phát tin, một worker để lưu tin.
        var payload = JsonSerializer.Serialize(acceptedEvent);
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);
        if (payloadBytes > MessageAcceptedEventV1.MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Message event payload vuot qua {MessageAcceptedEventV1.MaxPayloadBytes} bytes.");
        }

        // Tạo "dấu vết" cho lần gửi này theo người gửi + clientMessageId.
        // Nếu client gửi lại cùng tin vì mạng lỗi, hệ thống nhận ra đây là tin cũ chứ không tạo thêm tin mới.
        var sendMarkerKey = _keyProvider.BuildSendMarkerKey(
            acceptedEvent.SenderId,
            acceptedEvent.ClientMessageId);
        var acceptedAtUtc = acceptedEvent.AcceptedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        var idempotencyTtlMs = ((long)Math.Ceiling(_options.IdempotencyTtl.TotalMilliseconds))
            .ToString(CultureInfo.InvariantCulture);

        var database = _brokerConnection.GetDatabase();

        // Bước 3: đưa gói tin vào hàng đợi Redis để hai worker nền xử lý tiếp.
        // Script cũng ghi lại dấu vết chống trùng trong cùng một lần chạy.
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

        // Bước 4: đọc kết quả sau khi đưa vào hàng đợi.
        // "created" nghĩa là vừa tạo entry mới; "duplicate" nghĩa là tin này đã được đưa vào hàng đợi trước đó.
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

        // Kết quả này quay lại SendMessageCommandHandler, rồi trả cho frontend.
        // Frontend dùng clientMessageId để tìm tin tạm, còn messageId là mã chính thức của tin.
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
        // Không cho publish event sai version để tránh worker đọc nhầm schema.
        if (acceptedEvent.SchemaVersion != MessageAcceptedEventV1.CurrentSchemaVersion)
        {
            throw new ArgumentException("SchemaVersion cua message event khong hop le.", nameof(acceptedEvent));
        }

        // Các định danh này là tối thiểu để phát, lưu và truy vết một tin nhắn.
        if (acceptedEvent.CorrelationId == Guid.Empty ||
            acceptedEvent.ClientMessageId == Guid.Empty ||
            acceptedEvent.RoomId == Guid.Empty ||
            acceptedEvent.SenderId == Guid.Empty ||
            acceptedEvent.AcceptedAtUtc == default ||
            !ObjectId.TryParse(acceptedEvent.MessageId, out _))
        {
            throw new ArgumentException("Message event thieu dinh danh bat buoc.", nameof(acceptedEvent));
        }

        // Giữ giới hạn nội dung ở lớp broker để worker không nhận payload vượt chuẩn.
        var content = acceptedEvent.Content ?? string.Empty;
        if (content.Length > MessageAcceptedEventV1.MaxContentLength)
        {
            throw new ArgumentException("Noi dung message event vuot gioi han.", nameof(acceptedEvent));
        }

        var mediaIds = acceptedEvent.MediaIds ?? [];
        var attachments = acceptedEvent.Attachments ?? [];
        // Mỗi mediaId phải có đúng một snapshot attachment đi kèm để phát và lưu thống nhất.
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
