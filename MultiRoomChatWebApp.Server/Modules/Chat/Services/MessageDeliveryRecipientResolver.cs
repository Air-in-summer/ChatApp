using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class MessageDeliveryRecipientResolver : IMessageDeliveryRecipientResolver
{
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IGroupPermissionsCache _groupPermissionsCache;
    private readonly IUserRelationshipGraphService _relationshipGraphService;

    public MessageDeliveryRecipientResolver(
        IRoomMetadataCache roomMetadataCache,
        IRoomPermissionsCache roomPermissionsCache,
        IGroupPermissionsCache groupPermissionsCache,
        IUserRelationshipGraphService relationshipGraphService)
    {
        _roomMetadataCache = roomMetadataCache;
        _roomPermissionsCache = roomPermissionsCache;
        _groupPermissionsCache = groupPermissionsCache;
        _relationshipGraphService = relationshipGraphService;
    }

    public async Task<MessageRecipientResolutionResult> ResolveAsync(
        Guid roomId,
        Guid senderId,
        CancellationToken cancellationToken)
    {
        if (roomId == Guid.Empty || senderId == Guid.Empty)
        {
            return MessageRecipientResolutionResult.Failed(
                "delivery_context_invalid",
                "RoomId hoac SenderId khong hop le.");
        }

        var roomMetadata = await _roomMetadataCache.GetRoomMetadataAsync(roomId);
        if (roomMetadata is null)
        {
            return MessageRecipientResolutionResult.Failed(
                "delivery_room_not_found",
                "Khong tim thay metadata phong tai thoi diem delivery.");
        }

        if (roomMetadata.Value.Type == RoomType.Text &&
            roomMetadata.Value.GroupId.HasValue &&
            !roomMetadata.Value.IsPrivate)
        {
            var groupMembers = await _groupPermissionsCache.GetGroupMemberRolesAsync(
                roomMetadata.Value.GroupId.Value);
            var recipientIds = NormalizeRecipientIds(groupMembers.Keys);

            return recipientIds.Count == 0
                ? MessageRecipientResolutionResult.Failed(
                    "delivery_group_has_no_recipients",
                    "Nhom khong con thanh vien hop le tai thoi diem delivery.")
                : MessageRecipientResolutionResult.Resolved(
                    recipientIds,
                    roomMetadata.Value.Type,
                    roomMetadata.Value.GroupId);
        }

        var roomMembers = NormalizeRecipientIds(
            await _roomPermissionsCache.GetRoomMemberIdsAsync(roomId));

        if (roomMembers.Count == 0)
        {
            return MessageRecipientResolutionResult.Failed(
                "delivery_room_has_no_recipients",
                "Phong khong con thanh vien hop le tai thoi diem delivery.");
        }

        if (roomMetadata.Value.Type != RoomType.DirectMessage)
        {
            return MessageRecipientResolutionResult.Resolved(
                roomMembers,
                roomMetadata.Value.Type,
                roomMetadata.Value.GroupId);
        }

        if (roomMembers.Count != 2 || !roomMembers.Contains(senderId))
        {
            return MessageRecipientResolutionResult.Failed(
                "delivery_dm_members_invalid",
                "Phong DM khong con dung hai thanh vien hoac khong con nguoi gui.");
        }

        var otherUserId = roomMembers.First(userId => userId != senderId);
        var hasBlock = await _relationshipGraphService.HasBlockBetweenAsync(
            senderId,
            otherUserId,
            cancellationToken);

        if (hasBlock)
        {
            return MessageRecipientResolutionResult.Resolved(
                [senderId],
                roomMetadata.Value.Type,
                roomMetadata.Value.GroupId,
                isPeerDeliverySuppressed: true);
        }

        return MessageRecipientResolutionResult.Resolved(
            roomMembers,
            roomMetadata.Value.Type,
            roomMetadata.Value.GroupId);
    }

    private static IReadOnlyList<Guid> NormalizeRecipientIds(IEnumerable<Guid> userIds)
    {
        return userIds
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .ToList();
    }
}
