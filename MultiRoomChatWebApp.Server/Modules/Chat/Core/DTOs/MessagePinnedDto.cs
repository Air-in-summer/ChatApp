namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Trang thai ghim cua tin nhan.
/// </summary>
public sealed record MessagePinnedDto(
    Guid RoomId,
    string MessageId,
    DateTime PinnedAtUtc,
    Guid PinnedBy);
