using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.Cache;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <inheritdoc />
public sealed class MessageAdmissionService : IMessageAdmissionService
{
    private static readonly TimeSpan DirectMessageAllowPolicyTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DirectMessageBlockPolicyTtl = TimeSpan.FromDays(1);

    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly IGroupPermissionsCache _groupPermissionsCache;
    private readonly IUserRelationshipGraphService _relationshipGraphService;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MessageAdmissionService> _logger;

    public MessageAdmissionService(
        IRoomPermissionsCache roomPermissionsCache,
        IRoomMetadataCache roomMetadataCache,
        IGroupPermissionsCache groupPermissionsCache,
        IUserRelationshipGraphService relationshipGraphService,
        IConnectionMultiplexer redis,
        ILogger<MessageAdmissionService> logger)
    {
        _roomPermissionsCache = roomPermissionsCache;
        _roomMetadataCache = roomMetadataCache;
        _groupPermissionsCache = groupPermissionsCache;
        _relationshipGraphService = relationshipGraphService;
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MessageAdmissionResult> AdmitAsync(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        var mediaIds = (request.MediaIds ?? [])
            .Where(mediaId => mediaId != Guid.Empty)
            .Distinct()
            .ToList();

        if (request.RoomId == Guid.Empty)
        {
            return RejectValidation("room_id_empty", "Phong chat khong hop le.", request);
        }

        if (request.SenderId == Guid.Empty)
        {
            return RejectValidation("sender_id_empty", "Nguoi gui khong hop le.", request);
        }

        if (request.ClientMessageId == Guid.Empty)
        {
            return RejectValidation("client_message_id_empty", "Ma tam thoi cua tin nhan khong hop le.", request);
        }

        if (string.IsNullOrWhiteSpace(content) && mediaIds.Count == 0)
        {
            return RejectValidation("message_empty", "Noi dung tin nhan khong duoc de trong.", request);
        }

        if (content.Length > MessageAcceptedEventV1.MaxContentLength)
        {
            return RejectValidation("message_content_too_long", "Noi dung tin nhan vuot qua gioi han cho phep.", request);
        }

        if (mediaIds.Count > MessageAcceptedEventV1.MaxAttachmentCount)
        {
            return RejectValidation("message_attachment_count_too_large", "So luong tep dinh kem vuot qua gioi han cho phep.", request);
        }

        var roomMeta = await _roomMetadataCache.GetRoomMetadataAsync(request.RoomId);
        if (roomMeta is null)
        {
            _logger.LogWarning(
                "Admission rejected: Room {RoomId} does not exist or metadata is unavailable.",
                request.RoomId);
            return MessageAdmissionResult.Rejected(
                MessageAdmissionError.Forbidden(
                    "room_not_found_or_forbidden",
                    "Phong chat khong ton tai hoac ban khong co quyen."));
        }

        var isAuthorized = await IsAuthorizedAsync(
            request.RoomId,
            request.SenderId,
            roomMeta.Value.GroupId,
            roomMeta.Value.IsPrivate);

        if (!isAuthorized)
        {
            _logger.LogWarning(
                "Admission rejected: User {UserId} is not allowed to send to Room {RoomId}.",
                request.SenderId,
                request.RoomId);
            return MessageAdmissionResult.Rejected(
                MessageAdmissionError.Forbidden(
                    "room_forbidden",
                    "Ban khong co quyen gui tin vao phong nay."));
        }

        if (roomMeta.Value.Type == RoomType.DirectMessage &&
            !await IsDirectMessageAllowedAsync(request.RoomId, request.SenderId, cancellationToken))
        {
            _logger.LogWarning(
                "Admission rejected: User {UserId} cannot send DM to Room {RoomId} due to relationship policy.",
                request.SenderId,
                request.RoomId);
            return MessageAdmissionResult.Rejected(
                MessageAdmissionError.Forbidden(
                    "dm_not_allowed",
                    "Ban khong the gui tin nhan rieng toi nguoi dung nay."));
        }

        return MessageAdmissionResult.Accepted(
            new MessageAdmissionContext(
                request.RoomId,
                request.SenderId,
                request.ClientMessageId,
                content,
                mediaIds,
                roomMeta.Value.GroupId,
                roomMeta.Value.IsPrivate,
                roomMeta.Value.Type));
    }

    private async Task<bool> IsAuthorizedAsync(
        Guid roomId,
        Guid senderId,
        Guid? groupId,
        bool isPrivateRoom)
    {
        if (groupId.HasValue && !isPrivateRoom)
        {
            return await _groupPermissionsCache.IsUserInGroupAsync(groupId.Value, senderId);
        }

        return await _roomPermissionsCache.IsUserInRoomAsync(roomId, senderId);
    }

    private async Task<bool> IsDirectMessageAllowedAsync(
        Guid roomId,
        Guid senderId,
        CancellationToken cancellationToken)
    {
        var memberIds = (await _roomPermissionsCache.GetRoomMemberIdsAsync(roomId))
            .Distinct()
            .ToList();

        if (memberIds.Count != 2 || !memberIds.Contains(senderId))
        {
            return false;
        }

        var otherUserId = memberIds.First(userId => userId != senderId);
        var blockKey = UserRelationshipCacheKeys.BlockBetween(senderId, otherUserId);
        var allowKey = UserRelationshipCacheKeys.AllowBetween(senderId, otherUserId);
        var db = _redis.GetDatabase();

        if (await db.KeyExistsAsync(blockKey))
        {
            return false;
        }

        if (await db.KeyExistsAsync(allowKey))
        {
            return true;
        }

        var canDirectMessage = await _relationshipGraphService.CanDirectMessageAsync(
            senderId,
            otherUserId,
            cancellationToken);

        if (canDirectMessage)
        {
            await db.StringSetAsync(allowKey, "1", DirectMessageAllowPolicyTtl);
            return true;
        }

        await db.StringSetAsync(blockKey, "1", DirectMessageBlockPolicyTtl);
        await db.KeyDeleteAsync(allowKey);
        return false;
    }

    private MessageAdmissionResult RejectValidation(
        string code,
        string clientMessage,
        SendMessageCommand request)
    {
        _logger.LogWarning(
            "Admission rejected: validation failed. Code={Code}, UserId={UserId}, RoomId={RoomId}",
            code,
            request.SenderId,
            request.RoomId);

        return MessageAdmissionResult.Rejected(
            MessageAdmissionError.Validation(code, clientMessage));
    }
}
