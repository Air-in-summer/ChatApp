namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Thong bao tin nhan khong con duoc ghim.
/// </summary>
public sealed record MessageUnpinnedDto(
    Guid RoomId,
    string MessageId);
