using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Exceptions;

/// <summary>
/// Exception noi bo cho admission path; Hub/API se map sang response o buoc sau.
/// </summary>
public sealed class MessageAdmissionException : Exception
{
    public MessageAdmissionException(MessageAdmissionError error)
        : base(error.ClientMessage)
    {
        Error = error;
    }

    public MessageAdmissionException(MessageAdmissionError error, Exception innerException)
        : base(error.ClientMessage, innerException)
    {
        Error = error;
    }

    public MessageAdmissionError Error { get; }
}
