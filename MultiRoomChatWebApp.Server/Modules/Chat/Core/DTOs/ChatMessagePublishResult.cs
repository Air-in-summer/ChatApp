namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record ChatMessagePublishResult(
    bool IsNewEvent,
    string StreamId,
    string MessageId,
    DateTime AcceptedAtUtc);
