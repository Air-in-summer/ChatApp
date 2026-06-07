namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Trang thai tin nhan sau khi sua noi dung.
/// </summary>
public sealed record MessageEditedDto(
    Guid RoomId,
    string MessageId,
    string Content,
    DateTime EditedAtUtc,
    DateTime UpdatedAtUtc);
