using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Hubs;

/// <summary>
/// Hub xử lý Websocket kết nối thời gian thực cho tính năng Chat.
/// Được gắn [Authorize] đảm bảo chỉ User gửi JWT token lên mới chui lọt.
/// </summary>
[Authorize]
public class ChatHub : Hub<IChatClient>
{
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

    /// <summary>
    /// Kích hoạt tự động khi 1 kết nối Websocket được thiết lập thành công.
    /// Ghi nhận Connection và tung tin báo hiệu lên luồng chung.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdString, out Guid currentUserId))
        {
            // Báo vào Tracker. Trả về True nếu đây là cửa sổ đầu tiên user này truy cập
            bool isOnline = await _tracker.UserConnected(currentUserId, Context.ConnectionId);

            if (isOnline)
            {
                // Báo cho MỌI NGƯỜI KHÁC biết ổng vừa lên mạng
                // Ở quy mô cực lớn có thể bị lag broadcast, lúc đó ta mới tối ưu báo cho list bạn bè thôi.
                await NotifyPresenceAudienceAsync(currentUserId, isOnline: true);
            }
        }

        // Bắt buộc gọi base method của Microsoft
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Kích hoạt khi tab trình duyệt đóng, user crash mạng, hoặc connection bị ngắt chủ động.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdString, out Guid currentUserId))
        {
            // Mất 1 kết nối (có thể user này tắt 1 tab nhưng vẫn đang mở tab khác)
            bool isOffline = await _tracker.UserDisconnected(currentUserId, Context.ConnectionId);

            if (isOffline)
            {
                // Nếu đây là cái phao cuối cùng -> Rụng hoàn toàn -> Broadcast cho all biết ổng sụp rồi
                await _userPresenceService.MarkOfflineAsync(currentUserId, DateTime.UtcNow);
                await NotifyPresenceAudienceAsync(currentUserId, isOnline: false);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client gọi định kỳ để gia hạn TTL connection trong Redis presence.
    /// </summary>
    public async Task Heartbeat()
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        var becameOnline = await _tracker.TouchHeartbeatAsync(currentUserId, Context.ConnectionId);

        if (becameOnline)
        {
            await NotifyPresenceAudienceAsync(currentUserId, isOnline: true);
        }
    }

    /// <summary>
    /// Tham gia kênh tín hiệu riêng (SignalR Group) để chỉ nhận tin của phòng đó.
    /// Frontend gọi method này ngay khi mở cửa sổ Chat với 1 Room.
    /// </summary>
    public async Task JoinRoom(Guid roomId)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        await EnsureCanJoinRoomAsync(roomId, currentUserId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId.ToString());
    }

    /// <summary>
    /// Ném lệnh đi. Trả Thread lại ngay lập tức.
    /// </summary>
    /// <param name="roomId">ID phòng chat (Guid)</param>
    /// <param name="content">Nội dung tin nhắn</param>
    /// <param name="tempId">ID tạm mà Frontend gán (ví dụ: temp-123) để Worker có thể callback update trạng thái</param>
    public async Task SendMessage(Guid roomId, string content, string tempId)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid currentUserId)) return;

        var command = new Core.Commands.SendMessageCommand
        {
            RoomId = roomId,
            SenderId = currentUserId,
            Content = content,
            TempId = tempId
        };

        // Giao việc cho MediatR Handler
        await _mediator.Send(command);
    }

    /// <summary>
    /// Bắn sự kiện đang gõ phím cho mọi người trong phòng biết.
    /// Không lưu DB. Chỉ bay trên RAM.
    /// </summary>
    public async Task TypingStarted(Guid roomId)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid currentUserId)) return;

        await Clients.OthersInGroup(roomId.ToString()).ReceiveTyping(currentUserId, roomId);
    }

    /// <summary>
    /// Bắn sự kiện ngừng gõ phím.
    /// </summary>
    public async Task TypingStopped(Guid roomId)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid currentUserId)) return;

        await Clients.OthersInGroup(roomId.ToString()).ReceiveTypingStopped(currentUserId, roomId);
    }

    /// <summary>
    /// Báo cáo đã xem tin nhắn.
    /// Lưu Upsert cực nhanh vào Redis Hash (Ghi đè, không sinh rác).
    /// Broadcast kèm roomId để Frontend cập nhật đúng phòng trong Zustand store.
    /// </summary>
    public async Task MarkAsRead(Guid roomId, string lastReadMessageId)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid currentUserId)) return;

        var db = _redis.GetDatabase();
        var key = $"Room:{roomId}:ReadReceipts";

        // 1. Lấy ID tin nhắn đã đọc gần nhất từ Redis
        var currentReadId = await db.HashGetAsync(key, currentUserId.ToString());

        // 2. Chỉ xử lý nếu chưa có dữ liệu hoặc tin nhắn mới 'mới hơn' tin cũ
        // So sánh chuỗi ObjectId (Ordinal) giúp xác định thứ tự thời gian chính xác
        if (!currentReadId.HasValue || string.Compare(lastReadMessageId, currentReadId.ToString(), StringComparison.Ordinal) > 0)
        {
            // Ghi đè vào Redis Hash
            await db.HashSetAsync(key, currentUserId.ToString(), lastReadMessageId);
            
            // 3. Chỉ gửi thông báo cho người khác nếu có sự thay đổi thực sự
            await Clients.OthersInGroup(roomId.ToString()).ReceiveReadReceipt(currentUserId, roomId, lastReadMessageId);
        }
    }

    private Guid GetCurrentUserIdOrThrow()
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdString, out var currentUserId)
            ? currentUserId
            : throw new HubException("Phiên đăng nhập không hợp lệ.");
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
            throw new HubException("Phòng chat không tồn tại.");

        if (roomMetadata.Value.Type != RoomType.DirectMessage)
            return;

        var memberIds = (await _roomPermissionsCache.GetRoomMemberIdsAsync(roomId))
            .Distinct()
            .ToList();

        if (memberIds.Count != 2 || !memberIds.Contains(currentUserId))
            throw new HubException("Bạn không có quyền vào phòng này.");

        var otherUserId = memberIds.First(id => id != currentUserId);
        if (!await _relationshipGraphService.CanDirectMessageAsync(currentUserId, otherUserId))
            throw new HubException("Không thể mở cuộc trò chuyện này.");
    }
}
