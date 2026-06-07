namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record MessageRetractedDto(
    Guid RoomId,
    Guid ClientMessageId,
    string MessageId,
    string Code,
    DateTime RetractedAtUtc);
