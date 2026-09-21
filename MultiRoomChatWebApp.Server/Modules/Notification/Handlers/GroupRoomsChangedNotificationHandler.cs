using MediatR;
using Microsoft.AspNetCore.SignalR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Lắng nghe sự kiện cấu trúc phòng của Group thay đổi (thêm/sửa/xóa phòng) 
/// và điều phối việc gửi thông báo đến các thành viên liên quan.
/// </summary>
public class GroupRoomsChangedNotificationHandler : INotificationHandler<GroupRoomsChangedEvent>
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<GroupRoomsChangedNotificationHandler> _logger;

    public GroupRoomsChangedNotificationHandler(
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<GroupRoomsChangedNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Phát tín hiệu SignalR (GroupRoomsUpdated) nhắc client tự động gọi lại API để làm mới danh sách phòng.
    /// </summary>
    public async Task Handle(GroupRoomsChangedEvent notification, CancellationToken cancellationToken)
    {
        // Loại bỏ các ID trùng lặp nếu có để tránh gửi thông báo thừa
        var memberIds = notification.MemberIds.Distinct().ToList();
        
        _logger.LogInformation(
            "Broadcasting GroupRoomsChanged notification for Group {GroupId} to {MemberCount} members",
            notification.GroupId,
            memberIds.Count);

        // Duyệt qua từng thành viên và đẩy tín hiệu realtime qua Hub
        foreach (var userId in memberIds)
        {
            try
            {
                await _hubContext.Clients.User(userId.ToString())
                    .GroupRoomsUpdated(notification.GroupId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send GroupRoomsUpdated notification to User {UserId}",
                    userId);
            }
        }
    }
}
