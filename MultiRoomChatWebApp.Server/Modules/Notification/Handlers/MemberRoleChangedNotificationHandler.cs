using Microsoft.AspNetCore.SignalR;
using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Handler xử lý gửi thông báo khi một người dùng được thay đổi chức vụ.
/// </summary>
public class MemberRoleChangedNotificationHandler : INotificationHandler<MemberRoleUpdatedEvent>
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<MemberRoleChangedNotificationHandler> _logger;

    public MemberRoleChangedNotificationHandler(
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<MemberRoleChangedNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Handle(MemberRoleUpdatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting MemberRoleChanged notification to User {UserId} in Group {GroupId}", 
            notification.TargetUserId, notification.GroupId);

        try
        {
            await _hubContext.Clients.User(notification.TargetUserId.ToString())
                .MemberRoleChanged(notification.GroupId, notification.TargetUserId, notification.NewRole.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send MemberRoleChanged notification to User {UserId}", notification.TargetUserId);
        }
    }
}
