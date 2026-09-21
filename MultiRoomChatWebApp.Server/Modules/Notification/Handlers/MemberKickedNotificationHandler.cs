using Microsoft.AspNetCore.SignalR;
using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Handler xử lý gửi thông báo khi một người dùng bị trục xuất (Kick) khỏi Group.
/// </summary>
public class MemberKickedNotificationHandler : INotificationHandler<MemberKickedFromGroupEvent>
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly IGroupMetadataCache _metadataCache;
    private readonly ILogger<MemberKickedNotificationHandler> _logger;

    public MemberKickedNotificationHandler(
        IHubContext<ChatHub, IChatClient> hubContext,
        IGroupMetadataCache metadataCache,
        ILogger<MemberKickedNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _metadataCache = metadataCache;
        _logger = logger;
    }

    /// <summary>
    /// Phát tín hiệu SignalR (YouWereKicked) trực tiếp tới user vừa bị trục xuất khỏi Server.
    /// </summary>
    public async Task Handle(MemberKickedFromGroupEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting YouWereKicked notification to User {UserId} for Group {GroupId}", 
            notification.KickedUserId, notification.GroupId);

        // Lấy tên Group từ Cache để thông báo hiển thị rõ ràng
        var groupMetadata = await _metadataCache.GetGroupMetadataAsync(notification.GroupId);
        var groupName = groupMetadata?.Name ?? "Một Server";

        try
        {
            await _hubContext.Clients.User(notification.KickedUserId.ToString())
                .YouWereKicked(notification.GroupId, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send YouWereKicked notification to User {UserId}", notification.KickedUserId);
        }
    }
}
