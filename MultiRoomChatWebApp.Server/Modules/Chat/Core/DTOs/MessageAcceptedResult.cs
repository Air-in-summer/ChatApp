namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Kết quả xác nhận hệ thống đã tiếp nhận tin nhắn.
/// </summary>
/// <param name="ClientMessageId">Mã định danh do client sinh ra (UUID) để đối chiếu tạm thời (tránh gửi trùng).</param>
/// <param name="MessageId">Định danh chính thức của tin nhắn trên hệ thống Server.</param>
/// <param name="StreamId">Định danh luồng chat (Room) chứa tin nhắn.</param>
/// <param name="AcceptedAtUtc">Thời gian Server chấp nhận tin nhắn.</param>
public sealed record MessageAcceptedResult(
    Guid ClientMessageId,
    string MessageId,
    string StreamId,
    DateTime AcceptedAtUtc);
