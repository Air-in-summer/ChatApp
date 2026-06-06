using MediatR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.User.Core.Cache;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using System.Text.Json;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Handlers;

/// <summary>
/// Luồng xử lý cho SendMessageCommand.
/// DM block policy đi qua Redis cache; chỉ fallback relationship graph khi cache miss, không query relationship SQL theo từng tin.
/// </summary>
public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, bool>
{
    private static readonly TimeSpan DirectMessageAllowPolicyTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DirectMessageBlockPolicyTtl = TimeSpan.FromDays(1);

    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly Modules.Group.Core.Interfaces.IGroupPermissionsCache _groupPermissionsCache;
    private readonly IUserRelationshipGraphService _relationshipGraphService;
    private readonly AppDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    public SendMessageCommandHandler(
        IRoomPermissionsCache roomPermissionsCache, 
        IRoomMetadataCache roomMetadataCache,
        Modules.Group.Core.Interfaces.IGroupPermissionsCache groupPermissionsCache,
        IUserRelationshipGraphService relationshipGraphService,
        AppDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<SendMessageCommandHandler> logger)
    {
        _roomPermissionsCache = roomPermissionsCache;
        _roomMetadataCache = roomMetadataCache;
        _groupPermissionsCache = groupPermissionsCache;
        _relationshipGraphService = relationshipGraphService;
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Xử lý gửi tin nhắn với cơ chế Phân tầng Bảo mật (Hierarchical Auth).
    /// </summary>
    public async Task<bool> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        request.Content = request.Content?.Trim() ?? string.Empty;
        request.MediaIds = (request.MediaIds ?? [])
            .Where(mediaId => mediaId != Guid.Empty)
            .Distinct()
            .ToList();

        if (string.IsNullOrWhiteSpace(request.Content) && request.MediaIds.Count == 0)
        {
            _logger.LogWarning(
                "Bao mat: User {UserId} gui message rong vao Room {RoomId}.",
                request.SenderId,
                request.RoomId);
            return false;
        }

        // 1. Lấy Metadata của Phòng
        var roomMeta = await _roomMetadataCache.GetRoomMetadataAsync(request.RoomId);
        
        if (roomMeta == null)
        {
            _logger.LogWarning("Phòng {RoomId} không tồn tại hoặc đã bị xóa.", request.RoomId);
            return false;
        }

        // 2. DISPATCHER: Quyết định cách check quyền
        bool isAuthorized = false;

        if (roomMeta.Value.GroupId.HasValue && !roomMeta.Value.IsPrivate)
        {
            // TẦNG 1: PHÒNG PUBLIC TRONG GROUP -> Check Group Cache (Tránh nhồi 500 member vào Room Cache)
            isAuthorized = await _groupPermissionsCache.IsUserInGroupAsync(roomMeta.Value.GroupId.Value, request.SenderId);
        }
        else
        {
            // TẦNG 2: PHÒNG PRIVATE HOẶC DM -> Check Room Cache (Tối ưu cho phòng ít người)
            isAuthorized = await _roomPermissionsCache.IsUserInRoomAsync(request.RoomId, request.SenderId);
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Bảo mật: User {UserId} cố gắng nhắn tin vào Room {RoomId} mà không có quyền.", request.SenderId, request.RoomId);
            return false; 
        }

        // 3. Đóng gói payload
        if (roomMeta.Value.Type == RoomType.DirectMessage &&
            !await IsDirectMessageAllowedAsync(request.RoomId, request.SenderId, cancellationToken))
        {
            _logger.LogWarning(
                "Bao mat: User {UserId} bi chan gui tin DM vao Room {RoomId} boi relationship policy.",
                request.SenderId,
                request.RoomId);
            return false;
        }

        if (request.MediaIds.Count > 0 &&
            !await ArePendingMediaValidAsync(request.MediaIds, request.SenderId, request.RoomId, cancellationToken))
        {
            return false;
        }

        var messagePayload = JsonSerializer.Serialize(request);

        // 4. Ném vào Redis Streams chờ Worker xử lý
        var db = _redis.GetDatabase();
        await db.StreamAddAsync("chat_messages_stream", "payload", messagePayload);

        return true;
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
            return false;

        var otherUserId = memberIds.First(userId => userId != senderId);
        var blockKey = UserRelationshipCacheKeys.BlockBetween(senderId, otherUserId);
        var allowKey = UserRelationshipCacheKeys.AllowBetween(senderId, otherUserId);
        var db = _redis.GetDatabase();

        if (await db.KeyExistsAsync(blockKey))
            return false;

        if (await db.KeyExistsAsync(allowKey))
            return true;

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

    private async Task<bool> ArePendingMediaValidAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid senderId,
        Guid roomId,
        CancellationToken cancellationToken)
    {
        var mediaAssets = await _dbContext.MediaAssets
            .AsNoTracking()
            .Where(asset => mediaIds.Contains(asset.Id))
            .Select(asset => new
            {
                asset.Id,
                asset.OwnerUserId,
                asset.Scope,
                asset.Status,
                asset.RoomId,
                asset.DeletedAt
            })
            .ToListAsync(cancellationToken);

        if (mediaAssets.Count != mediaIds.Count)
        {
            _logger.LogWarning(
                "Bao mat: User {UserId} gui media khong ton tai vao Room {RoomId}. Expected={Expected}; Actual={Actual}",
                senderId,
                roomId,
                mediaIds.Count,
                mediaAssets.Count);
            return false;
        }

        var invalidMedia = mediaAssets.FirstOrDefault(asset =>
            asset.Scope != MediaScope.ChatAttachment ||
            asset.OwnerUserId != senderId ||
            asset.Status != MediaAssetStatus.Pending ||
            asset.RoomId != null ||
            asset.DeletedAt != null);

        if (invalidMedia == null)
            return true;

        _logger.LogWarning(
            "Bao mat: User {UserId} gui media {MediaId} khong hop le vao Room {RoomId}. Scope={Scope}; Status={Status}; RoomId={MediaRoomId}; Owner={OwnerUserId}; DeletedAt={DeletedAt}",
            senderId,
            invalidMedia.Id,
            roomId,
            invalidMedia.Scope,
            invalidMedia.Status,
            invalidMedia.RoomId,
            invalidMedia.OwnerUserId,
            invalidMedia.DeletedAt);

        return false;
    }
}
