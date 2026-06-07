using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Exceptions;

public sealed class ChatBrokerEntryProcessingException : Exception
{
    public ChatBrokerEntryProcessingException(
        ChatBrokerEntryFailureKind failureKind,
        string code,
        string message)
        : base(message)
    {
        FailureKind = failureKind;
        Code = code;
    }

    public ChatBrokerEntryFailureKind FailureKind { get; }

    public string Code { get; }

    public static ChatBrokerEntryProcessingException Permanent(
        string code,
        string message)
        => new(ChatBrokerEntryFailureKind.Permanent, code, message);
}
