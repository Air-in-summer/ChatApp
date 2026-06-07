using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Kiem tra quyen truy cap phong va quyen quan tri cho message mutation.
/// </summary>
public sealed class MessageMutationPermissionService : IMessageMutationPermissionService
{
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IGroupPermissionsCache _groupPermissionsCache;

    public MessageMutationPermissionService(
        IRoomMetadataCache roomMetadataCache,
        IRoomPermissionsCache roomPermissionsCache,
        IGroupPermissionsCache groupPermissionsCache)
    {
        _roomMetadataCache = roomMetadataCache;
        _roomPermissionsCache = roomPermissionsCache;
        _groupPermissionsCache = groupPermissionsCache;
    }

    /// <inheritdoc />
    public async Task<MessageMutationRoomContext> EnsureCanReadRoomAsync(
        Guid roomId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (roomId == Guid.Empty || userId == Guid.Empty)
        {
            throw ApiException.BadRequest(
                "message_mutation_context_invalid",
                "Phong hoac nguoi dung khong hop le.");
        }

        var metadata = await _roomMetadataCache.GetRoomMetadataAsync(roomId);
        if (metadata is null)
        {
            throw ApiException.NotFound(
                "message_room_not_found",
                "Phong chat khong ton tai hoac da bi xoa.");
        }

        if (metadata.Value.Type == RoomType.Voice)
        {
            throw ApiException.Conflict(
                "message_room_type_not_supported",
                "Phong thoai khong ho tro thao tac tin nhan.");
        }

        var canRead = metadata.Value.GroupId.HasValue && !metadata.Value.IsPrivate
            ? await _groupPermissionsCache.IsUserInGroupAsync(
                metadata.Value.GroupId.Value,
                userId)
            : await _roomPermissionsCache.IsUserInRoomAsync(roomId, userId);

        if (!canRead)
        {
            throw ApiException.Forbidden(
                "message_room_forbidden",
                "Ban khong con quyen truy cap phong chat nay.");
        }

        return new MessageMutationRoomContext(
            roomId,
            metadata.Value.Type,
            metadata.Value.GroupId,
            metadata.Value.IsPrivate);
    }

    /// <inheritdoc />
    public bool IsAuthor(Message message, Guid userId)
    {
        return message.SenderId == userId;
    }

    /// <inheritdoc />
    public async Task<bool> IsGroupRoomModeratorAsync(
        MessageMutationRoomContext roomContext,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!roomContext.GroupId.HasValue ||
            roomContext.RoomType == RoomType.DirectMessage)
        {
            return false;
        }

        var role = await _groupPermissionsCache.GetMemberRoleAsync(
            roomContext.GroupId.Value,
            userId);

        return role is GroupRole.Owner or GroupRole.Admin;
    }
}
