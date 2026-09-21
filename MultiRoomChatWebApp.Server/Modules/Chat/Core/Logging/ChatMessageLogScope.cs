using System.Text.Json;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Logging;

/// <summary>
/// Cung cấp các phương thức tiện ích để tạo phạm vi log (Logging Scope) có cấu trúc, 
/// giúp theo dõi và liên kết thống nhất các log liên quan đến toàn bộ vòng đời của một tin nhắn.
/// </summary>
public static class ChatMessageLogScope
{
    /// <summary>
    /// Khởi tạo một Structured Logging Scope với các thông tin định danh chi tiết.
    /// </summary>
    /// <param name="logger">Đơn vị ghi log thực thi.</param>
    /// <param name="messageId">Định danh chính thức của tin nhắn trên hệ thống Server.</param>
    /// <param name="clientMessageId">Định danh tạm thời do Client cung cấp (nếu có).</param>
    /// <param name="streamId">Định danh luồng dữ liệu (Stream) từ Message Broker.</param>
    /// <param name="roomId">Định danh phòng chat nơi tin nhắn được gửi.</param>
    /// <param name="senderId">Định danh người gửi tin nhắn.</param>
    /// <param name="group">Tên của Consumer Group đang xử lý luồng sự kiện này.</param>
    /// <param name="consumer">Tên của Consumer (Worker) cụ thể đang thực hiện tác vụ.</param>
    /// <param name="attempt">Số lần đã thử xử lý (Retry count).</param>
    /// <param name="correlationId">Mã định vị theo dõi (Correlation ID) xuyên suốt các dịch vụ phân tán.</param>
    /// <returns>Đối tượng IDisposable để tự động giải phóng (đóng scope) khi kết thúc block using.</returns>
    public static IDisposable? Begin(
        ILogger logger,
        string? messageId,
        Guid? clientMessageId,
        string? streamId,
        Guid? roomId,
        Guid? senderId,
        string group,
        string consumer,
        int attempt,
        Guid? correlationId)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = messageId ?? string.Empty,
            ["ClientMessageId"] = clientMessageId,
            ["StreamId"] = streamId ?? string.Empty,
            ["RoomId"] = roomId,
            ["SenderId"] = senderId,
            ["Group"] = group,
            ["Consumer"] = consumer,
            ["Attempt"] = attempt,
            ["CorrelationId"] = correlationId
        });
    }

    /// <summary>
    /// Phân tích và trích xuất dữ liệu từ payload (chuỗi JSON) để tạo Logging Scope tự động.
    /// Thường dùng ngay tại đầu vào của một quá trình đọc dữ liệu từ Broker.
    /// </summary>
    /// <param name="logger">Đơn vị ghi log thực thi.</param>
    /// <param name="streamId">Định danh luồng dữ liệu (Stream).</param>
    /// <param name="group">Tên của Consumer Group.</param>
    /// <param name="consumer">Tên của Consumer (Worker).</param>
    /// <param name="attempt">Số lần đã thử xử lý.</param>
    /// <param name="payload">Dữ liệu thô (chuỗi JSON) chứa thông tin sự kiện tin nhắn.</param>
    /// <returns>Đối tượng IDisposable để tự động đóng scope.</returns>
    public static IDisposable? BeginForEntry(
        ILogger logger,
        string streamId,
        string group,
        string consumer,
        int attempt,
        string? payload)
    {
        var acceptedEvent = TryReadEvent(payload);
        return Begin(
            logger,
            acceptedEvent?.MessageId,
            acceptedEvent?.ClientMessageId,
            streamId,
            acceptedEvent?.RoomId,
            acceptedEvent?.SenderId,
            group,
            consumer,
            attempt,
            acceptedEvent?.CorrelationId);
    }

    /// <summary>
    /// Cố gắng parse an toàn chuỗi JSON sang đối tượng MessageAcceptedEventV1 mà không văng Exception nếu chuỗi lỗi.
    /// </summary>
    private static MessageAcceptedEventV1? TryReadEvent(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MessageAcceptedEventV1>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
