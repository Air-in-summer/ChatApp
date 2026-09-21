namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Kết quả xuất bản (Publish) tin nhắn vào Broker.
/// </summary>
/// <param name="IsNewEvent">Cho biết đây là tin nhắn mới tinh (true) hay là tin nhắn bị đẩy lại trùng lặp (false).</param>
/// <param name="StreamId">Định danh của Stream chứa tin nhắn (thường là RoomId).</param>
/// <param name="MessageId">Định danh duy nhất của tin nhắn được cấp phát trên Broker.</param>
/// <param name="AcceptedAtUtc">Thời điểm Broker xác nhận đã tiếp nhận tin nhắn.</param>
public sealed record ChatMessagePublishResult(
    bool IsNewEvent,
    string StreamId,
    string MessageId,
    DateTime AcceptedAtUtc);
