using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Loi co kieu ro rang cua admission path khi tiep nhan tin nhan.
/// </summary>
public sealed record MessageAdmissionError(
    MessageAdmissionErrorKind Kind,
    string Code,
    string ClientMessage,
    bool IsRetryable)
{
    public static MessageAdmissionError Validation(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.Validation, code, clientMessage, IsRetryable: false);

    public static MessageAdmissionError Forbidden(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.Forbidden, code, clientMessage, IsRetryable: false);

    public static MessageAdmissionError Conflict(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.Conflict, code, clientMessage, IsRetryable: false);

    public static MessageAdmissionError BrokerUnavailable(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.BrokerUnavailable, code, clientMessage, IsRetryable: true);
}
