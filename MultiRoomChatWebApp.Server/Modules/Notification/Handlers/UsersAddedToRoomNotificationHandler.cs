using Microsoft.AspNetCore.SignalR;
using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Handler xử lý gửi thông báo SignalR khi có thành viên mới được thêm vào phòng Private.
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

    public async Task Handle(UsersAddedToPrivateRoomEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting GroupRoomsUpdated notification to {Count} added users for Room {RoomId} in Group {GroupId}", 
            notification.AddedUserIds.Count(), notification.RoomId, notification.GroupId);

        try
        {
            // [TRICK]: Ta sử dụng tín hiệu GroupRoomsUpdated(groupId) có sẵn.
            // Khi nhận được, Client sẽ tự động refetch danh sách phòng của Group đó.
            // Vì User vừa được add vào RoomMembers, nên API trả về sẽ chứa thêm phòng Private này.
            
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
