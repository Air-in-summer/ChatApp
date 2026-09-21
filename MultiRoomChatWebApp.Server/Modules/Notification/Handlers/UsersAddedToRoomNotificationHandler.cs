using Microsoft.AspNetCore.SignalR;
using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Lắng nghe sự kiện thêm thành viên mới vào một kênh bí mật (Private Room) 
/// để chủ động gửi thông báo tải lại phòng tới những người đó.
/// </summary>
public class UsersAddedToRoomNotificationHandler : INotificationHandler<UsersAddedToPrivateRoomEvent>
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<UsersAddedToRoomNotificationHandler> _logger;

    public UsersAddedToRoomNotificationHandler(
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<UsersAddedToRoomNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Phát tín hiệu SignalR ép client của những người dùng mới tự động fetch lại danh sách kênh.
    /// </summary>
    public async Task Handle(UsersAddedToPrivateRoomEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting GroupRoomsUpdated notification to {Count} added users for Room {RoomId} in Group {GroupId}", 
            notification.AddedUserIds.Count(), notification.RoomId, notification.GroupId);

        try
        {
            // Thay vì tạo ra một sự kiện báo có phòng mới riêng biệt,
            // ta tái sử dụng tín hiệu GroupRoomsUpdated.
            // Khi nhận được, Client sẽ tự gọi API lấy lại toàn bộ danh sách phòng Private mà nó được truy cập.
            foreach (var userId in notification.AddedUserIds)
            {
                await _hubContext.Clients.User(userId.ToString())
                    .GroupRoomsUpdated(notification.GroupId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast UsersAddedToRoom notification for Group {GroupId}", notification.GroupId);
        }
    }
}
