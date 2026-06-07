namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record MessagePersistedDto(
    Guid RoomId,
    Guid ClientMessageId,
    string MessageId,
    DateTime PersistedAtUtc,
    string Status);
