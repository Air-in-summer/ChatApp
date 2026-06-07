namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Tombstone duoc phat khi tin nhan bi xoa voi moi nguoi.
/// </summary>
public sealed record MessageDeletedDto(
    Guid RoomId,
    string MessageId,
    DateTime DeletedAtUtc,
    Guid DeletedBy);
