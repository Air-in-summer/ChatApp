namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record MessageAdmissionResult(
    MessageAdmissionContext? Context,
    MessageAdmissionError? Error)
{
    public bool IsAccepted => Context is not null && Error is null;

    public static MessageAdmissionResult Accepted(MessageAdmissionContext context)
        => new(context, null);

    public static MessageAdmissionResult Rejected(MessageAdmissionError error)
        => new(null, error);
}
