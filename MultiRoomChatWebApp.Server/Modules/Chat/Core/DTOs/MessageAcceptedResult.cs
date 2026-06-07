namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Kết quả trả về ngay sau khi yêu cầu gửi tin đã được tiếp nhận vào hàng đợi.
/// </summary>
public sealed record MessageAcceptedResult(
    Guid ClientMessageId,
    string MessageId,
    string StreamId,
    DateTime AcceptedAtUtc);
