using Microsoft.AspNetCore.SignalR;
using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Handler xử lý việc gửi thông báo Real-time khi có một Channel (Room) mới được tạo trong Group.
/// Giúp giải quyết vấn đề đồng bộ UI (Bug #2) mà không cần User phải F5.
/// </summary>
public class RoomCreatedNotificationHandler : INotificationHandler<RoomCreatedInGroupEvent>
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<RoomCreatedNotificationHandler> _logger;

    public RoomCreatedNotificationHandler(
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<RoomCreatedNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Luồng xử lý:
    /// 1. Nhận sự kiện RoomCreatedInGroupEvent.
    /// 2. Duyệt qua danh sách MemberIds của Group.
    /// 3. Sử dụng SignalR HubContext để gửi tín hiệu "GroupRoomsUpdated" tới từng User.
    /// </summary>
    public async Task Handle(RoomCreatedInGroupEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting RoomCreated notification for Room {RoomId} in Group {GroupId} to {MemberCount} members", 
            notification.RoomId, notification.GroupId, notification.MemberIds.Count());

        // Sử dụng Clients.User(id) để gửi đích danh dựa trên NameIdentifier claim.
        // SignalR sẽ tự động tìm tất cả các connection active của User này để đẩy tín hiệu.
        foreach (var userId in notification.MemberIds)
        {
            try
            {
                await _hubContext.Clients.User(userId.ToString())
                    .GroupRoomsUpdated(notification.GroupId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SignalR notification to User {UserId}", userId);
            }
        }
    }
}
