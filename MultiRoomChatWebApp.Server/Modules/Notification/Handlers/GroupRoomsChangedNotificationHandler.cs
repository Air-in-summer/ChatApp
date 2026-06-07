using MediatR;
using Microsoft.AspNetCore.SignalR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Gui tin hieu realtime khi danh sach phong trong group bi doi ten hoac xoa.
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

    public async Task Handle(GroupRoomsChangedEvent notification, CancellationToken cancellationToken)
    {
        var memberIds = notification.MemberIds.Distinct().ToList();
        _logger.LogInformation(
            "Broadcasting GroupRoomsChanged notification for Group {GroupId} to {MemberCount} members",
            notification.GroupId,
            memberIds.Count);

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
