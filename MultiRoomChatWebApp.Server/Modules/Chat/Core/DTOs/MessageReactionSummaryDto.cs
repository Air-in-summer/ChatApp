namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Tong hop reaction theo emoji cho mot nguoi dung dang xem.
/// </summary>
public sealed record MessageReactionSummaryDto(
    string Emoji,
    int Count,
    bool CurrentUserReacted);
