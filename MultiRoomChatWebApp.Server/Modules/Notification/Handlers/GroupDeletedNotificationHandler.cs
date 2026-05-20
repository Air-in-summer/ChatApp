using Microsoft.AspNetCore.SignalR;
using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Handler xử lý gửi thông báo khi một Group bị giải tán (Soft Delete).
/// </summary>
public class GroupDeletedNotificationHandler : INotificationHandler<GroupDeletedEvent>
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly IGroupMetadataCache _metadataCache;
    private readonly ILogger<GroupDeletedNotificationHandler> _logger;

    public GroupDeletedNotificationHandler(
        IHubContext<ChatHub, IChatClient> hubContext,
        IGroupMetadataCache metadataCache,
        ILogger<GroupDeletedNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _metadataCache = metadataCache;
        _logger = logger;
    }

    public async Task Handle(GroupDeletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting GroupDeleted notification for Group {GroupId} to {MemberCount} members", 
            notification.GroupId, notification.MemberIds.Count());

        // Mặc dù Group có thể đã bị xóa trong DB (Soft Delete), cache có thể vẫn còn
        var groupMetadata = await _metadataCache.GetGroupMetadataAsync(notification.GroupId);
        var groupName = groupMetadata?.Name ?? "Một Server";

        // Gửi cho tất cả member (bao gồm cả owner nếu muốn)
        foreach (var userId in notification.MemberIds)
        {
            try
            {
                await _hubContext.Clients.User(userId.ToString())
                    .GroupDeleted(notification.GroupId, groupName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send GroupDeleted notification to User {UserId}", userId);
            }
        }
    }
}
