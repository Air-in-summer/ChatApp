namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record MessagePersistenceFailedDto(
    Guid RoomId,
    Guid ClientMessageId,
    string MessageId,
    string Code,
    DateTime FailedAtUtc);
