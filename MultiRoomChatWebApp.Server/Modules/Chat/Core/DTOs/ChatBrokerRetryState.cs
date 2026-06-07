namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record ChatBrokerRetryState(
    int Attempt,
    DateTime NextRetryAtUtc);
