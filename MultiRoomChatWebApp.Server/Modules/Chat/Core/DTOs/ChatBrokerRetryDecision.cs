using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record ChatBrokerRetryDecision(
    int Attempt,
    DateTime? NextRetryAtUtc,
    bool IsDue,
    bool IsExhausted,
    ChatBrokerEntryFailureKind? FailureKind,
    string? ErrorCode,
    string? LastError);
