using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Exceptions;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Hubs;

[Authorize]
public class ChatHub : Hub<IChatClient>
{
    private const string UpdateLegacyReceiptScript = """
        local currentMessageId = redis.call('HGET', KEYS[1], ARGV[1])
        if currentMessageId and currentMessageId >= ARGV[2] then
            return 0
        end

        redis.call('HSET', KEYS[1], ARGV[1], ARGV[2])
        return 1
        """;

    private readonly IPresenceTracker _tracker;
    private readonly MediatR.IMediator _mediator;
    private readonly StackExchange.Redis.IConnectionMultiplexer _redis;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IUserRelationshipGraphService _relationshipGraphService;
    private readonly IUserPresenceService _userPresenceService;

    public ChatHub(
        IPresenceTracker tracker,
        MediatR.IMediator mediator,
        StackExchange.Redis.IConnectionMultiplexer redis,
        IRoomMetadataCache roomMetadataCache,
        IRoomPermissionsCache roomPermissionsCache,
        IUserRelationshipGraphService relationshipGraphService,
        IUserPresenceService userPresenceService)
    {
        _tracker = tracker;
        _mediator = mediator;
        _redis = redis;
        _roomMetadataCache = roomMetadataCache;
        _roomPermissionsCache = roomPermissionsCache;
        _relationshipGraphService = relationshipGraphService;
        _userPresenceService = userPresenceService;
    }

    public override async Task OnConnectedAsync()
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdString, out var currentUserId))
        {
            var isOnline = await _tracker.UserConnected(currentUserId, Context.ConnectionId);
            if (isOnline)
            {
                await NotifyPresenceAudienceAsync(currentUserId, isOnline: true);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdString, out var currentUserId))
        {
            var isOffline = await _tracker.UserDisconnected(currentUserId, Context.ConnectionId);
            if (isOffline)
            {
                await _userPresenceService.MarkOfflineAsync(currentUserId, DateTime.UtcNow);
                await NotifyPresenceAudienceAsync(currentUserId, isOnline: false);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task Heartbeat()
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        var becameOnline = await _tracker.TouchHeartbeatAsync(currentUserId, Context.ConnectionId);

        if (becameOnline)
        {
            await NotifyPresenceAudienceAsync(currentUserId, isOnline: true);
        }
    }

    public async Task JoinRoom(Guid roomId)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        await EnsureCanJoinRoomAsync(roomId, currentUserId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId.ToString());
    }

    public async Task<MessageAcceptedResult> SendMessage(SendMessageRequest request)
    {
        if (request == null)
        {
            throw new HubException("Yeu cau gui tin khong hop le.");
        }

        var currentUserId = GetCurrentUserIdOrThrow();
        var mediaIds = (request.MediaIds ?? [])
            .Where(mediaId => mediaId != Guid.Empty)
            .Distinct()
            .ToList();

        var command = new Core.Commands.SendMessageCommand
        {
            RoomId = request.RoomId,
            SenderId = currentUserId,
            Content = request.Content ?? string.Empty,
            ClientMessageId = request.ClientMessageId,
            MediaIds = mediaIds
        };

        try
        {
            return await _mediator.Send(command);
        }
        catch (MessageAdmissionException ex)
        {
            throw new HubException(FormatAdmissionHubMessage(ex.Error));
        }
    }

    public async Task TypingStarted(Guid roomId)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var currentUserId))
        {
            return;
        }

        await Clients.OthersInGroup(roomId.ToString()).ReceiveTyping(currentUserId, roomId);
    }

    public async Task TypingStopped(Guid roomId)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var currentUserId))
        {
            return;
        }

        await Clients.OthersInGroup(roomId.ToString()).ReceiveTypingStopped(currentUserId, roomId);
    }

    /// <summary>
    /// Cap nhat moc da doc theo message ID cho client cu hoac message chua co sequence.
    /// </summary>
    public async Task MarkAsRead(Guid roomId, string lastReadMessageId)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        if (string.IsNullOrWhiteSpace(lastReadMessageId))
        {
            return;
        }

        await EnsureRoomMembershipAsync(roomId, currentUserId);

        var db = _redis.GetDatabase();
        var messageIdKey = GetReadReceiptMessageIdKey(roomId);
        var updated = (long)await db.ScriptEvaluateAsync(
            UpdateLegacyReceiptScript,
            [messageIdKey],
            [currentUserId.ToString(), lastReadMessageId]);

        if (updated == 1)
        {
            await Clients.OthersInGroup(roomId.ToString())
                .ReceiveReadReceipt(currentUserId, roomId, lastReadMessageId);
        }
    }

    private Guid GetCurrentUserIdOrThrow()
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdString, out var currentUserId)
            ? currentUserId
            : throw new HubException("Phien dang nhap khong hop le.");
    }

    private static RedisKey GetReadReceiptMessageIdKey(Guid roomId) =>
        $"Room:{roomId}:ReadReceipts";

    private async Task EnsureRoomMembershipAsync(Guid roomId, Guid currentUserId)
    {
        var memberIds = await _roomPermissionsCache.GetRoomMemberIdsAsync(roomId);
        if (!memberIds.Contains(currentUserId))
        {
            throw new HubException("Ban khong co quyen cap nhat trang thai doc cua phong nay.");
        }
    }

    private static string FormatAdmissionHubMessage(MessageAdmissionError error)
    {
        return $"{error.Code}|{error.ClientMessage}|retryable={error.IsRetryable.ToString().ToLowerInvariant()}";
    }

    private async Task NotifyPresenceAudienceAsync(Guid changedUserId, bool isOnline)
    {
        var audienceIds = await _relationshipGraphService.GetPresenceAudienceAsync(changedUserId);
        if (audienceIds.Count == 0)
        {
            return;
        }

        var clients = Clients.Users(audienceIds.Select(userId => userId.ToString()));
        if (isOnline)
        {
            await clients.UserIsOnline(changedUserId);
            return;
        }

        await clients.UserIsOffline(changedUserId);
    }

    private async Task EnsureCanJoinRoomAsync(Guid roomId, Guid currentUserId)
    {
        var roomMetadata = await _roomMetadataCache.GetRoomMetadataAsync(roomId);
        if (roomMetadata == null)
        {
            throw new HubException("Phong chat khong ton tai.");
        }

        if (roomMetadata.Value.Type != RoomType.DirectMessage)
        {
            return;
        }

        var memberIds = (await _roomPermissionsCache.GetRoomMemberIdsAsync(roomId))
            .Distinct()
            .ToList();

        if (memberIds.Count != 2 || !memberIds.Contains(currentUserId))
        {
            throw new HubException("Ban khong co quyen vao phong nay.");
        }

        var otherUserId = memberIds.First(id => id != currentUserId);
        if (!await _relationshipGraphService.CanDirectMessageAsync(currentUserId, otherUserId))
        {
            throw new HubException("Khong the mo cuoc tro chuyen nay.");
        }
    }
}
