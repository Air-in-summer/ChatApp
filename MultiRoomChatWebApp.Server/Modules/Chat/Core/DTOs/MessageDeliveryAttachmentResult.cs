using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record MessageDeliveryAttachmentResult(
    bool IsSuccess,
    IReadOnlyList<Attachment> Attachments,
    string? ErrorCode,
    string? ErrorReason)
{
    public static MessageDeliveryAttachmentResult Resolved(
        IReadOnlyList<Attachment> attachments)
        => new(true, attachments, null, null);

    public static MessageDeliveryAttachmentResult Failed(
        string errorCode,
        string errorReason)
        => new(false, [], errorCode, errorReason);
}
