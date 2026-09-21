using MediatR;
using Microsoft.AspNetCore.SignalR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Notification.Handlers;

/// <summary>
/// Quản lý vòng đời thông báo của các cuộc gọi đàm thoại trực tiếp (Voice Call 1-1).
/// Tiếp nhận các domain event từ module Voice và chuyển tiếp thành tín hiệu đổ chuông, bắt máy, hoặc kết thúc.
/// </summary>
public class VoiceCallNotificationHandler :
    INotificationHandler<VoiceCallIncomingEvent>,
    INotificationHandler<VoiceCallAcceptedEvent>,
    INotificationHandler<VoiceCallDeclinedEvent>,
    INotificationHandler<VoiceCallEndedEvent>
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<VoiceCallNotificationHandler> _logger;

    public VoiceCallNotificationHandler(
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<VoiceCallNotificationHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Phát tín hiệu đổ chuông cuộc gọi đến (VoiceCallIncoming) tới người nhận.
    /// </summary>
    public async Task Handle(VoiceCallIncomingEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.Clients.User(notification.TargetUserId.ToString())
                .VoiceCallIncoming(notification.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send VoiceCallIncoming to User {UserId} for Session {SessionId}",
                notification.TargetUserId,
                notification.Payload.Session.SessionId);
        }
    }

    /// <summary>
    /// Phát tín hiệu báo đối phương đã bắt máy (VoiceCallAccepted).
    /// </summary>
    public async Task Handle(VoiceCallAcceptedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.Clients.User(notification.TargetUserId.ToString())
                .VoiceCallAccepted(notification.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send VoiceCallAccepted to User {UserId} for Session {SessionId}",
                notification.TargetUserId,
                notification.Payload.Session.SessionId);
        }
    }

    /// <summary>
    /// Phát tín hiệu báo đối phương đã từ chối cuộc gọi (VoiceCallDeclined).
    /// </summary>
    public async Task Handle(VoiceCallDeclinedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.Clients.User(notification.TargetUserId.ToString())
                .VoiceCallDeclined(notification.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send VoiceCallDeclined to User {UserId} for Session {SessionId}",
                notification.TargetUserId,
                notification.Payload.Session.SessionId);
        }
    }

    /// <summary>
    /// Phát tín hiệu kết thúc cuộc gọi (VoiceCallEnded) tới các bên.
    /// </summary>
    public async Task Handle(VoiceCallEndedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var userId in notification.TargetUserIds.Distinct())
        {
            try
            {
                await _hubContext.Clients.User(userId.ToString())
                    .VoiceCallEnded(notification.Payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send VoiceCallEnded to User {UserId} for Session {SessionId}",
                    userId,
                    notification.Payload.Session.SessionId);
            }
        }
    }
}
