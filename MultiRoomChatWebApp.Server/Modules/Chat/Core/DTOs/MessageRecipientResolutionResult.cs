using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record MessageRecipientResolutionResult(
    bool IsSuccess,
    IReadOnlyList<Guid> RecipientIds,
    RoomType? RoomType,
    Guid? GroupId,
    bool IsPeerDeliverySuppressed,
    string? ErrorCode,
    string? ErrorReason)
{
    public static MessageRecipientResolutionResult Resolved(
        IReadOnlyList<Guid> recipientIds,
        RoomType roomType,
        Guid? groupId,
        bool isPeerDeliverySuppressed = false)
        => new(
            true,
            recipientIds,
            roomType,
            groupId,
            isPeerDeliverySuppressed,
            null,
            null);

    public static MessageRecipientResolutionResult Failed(
        string errorCode,
        string errorReason)
        => new(
            false,
            [],
            null,
            null,
            false,
            errorCode,
            errorReason);
}
