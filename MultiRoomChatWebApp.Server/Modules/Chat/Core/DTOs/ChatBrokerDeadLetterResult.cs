namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record ChatBrokerDeadLetterResult(
    bool IsDeadLettered,
    string? DeadLetterEntryId,
    int Attempt,
    long AcknowledgedCount);
