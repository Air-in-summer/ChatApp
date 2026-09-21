using Microsoft.AspNetCore.SignalR;
using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Lắng nghe sự kiện một thành viên bị thay đổi chức vụ (Role) trong Group 
/// và chịu trách nhiệm gửi thông báo đẩy đến người dùng đó.
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

    /// <summary>
    /// Phát tín hiệu SignalR (MemberRoleChanged) trực tiếp đến thiết bị của người dùng vừa bị thay đổi quyền.
    /// </summary>
    public async Task Handle(MemberRoleUpdatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting MemberRoleChanged notification to User {UserId} in Group {GroupId}", 
            notification.TargetUserId, notification.GroupId);

        try
        {
            // Gửi trực tiếp đến User mục tiêu (qua NameIdentifier)
            // Kèm theo thông tin GroupId và Tên của Role mới (đã parse sang chuỗi)
            await _hubContext.Clients.User(notification.TargetUserId.ToString())
                .MemberRoleChanged(notification.GroupId, notification.TargetUserId, notification.NewRole.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send MemberRoleChanged notification to User {UserId}", notification.TargetUserId);
        }
    }
}
