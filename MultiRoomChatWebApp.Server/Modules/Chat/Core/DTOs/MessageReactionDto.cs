namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Mot reaction cua mot nguoi dung tren tin nhan.
/// </summary>
public sealed record MessageReactionDto(
    string Emoji,
    Guid UserId,
    DateTime CreatedAtUtc);
