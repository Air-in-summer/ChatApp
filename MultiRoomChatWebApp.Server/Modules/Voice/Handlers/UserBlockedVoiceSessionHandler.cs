using MediatR;
using MultiRoomChatWebApp.Server.Modules.User.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Handlers;

/// <summary>
/// Dong cac DM call dang dien ra khi mot trong hai participant block nguoi con lai.
/// </summary>
public class UserBlockedVoiceSessionHandler : INotificationHandler<UserBlockedEvent>
{
    private readonly IVoiceSessionService _voiceSessionService;
    private readonly ILogger<UserBlockedVoiceSessionHandler> _logger;

    public UserBlockedVoiceSessionHandler(
        IVoiceSessionService voiceSessionService,
        ILogger<UserBlockedVoiceSessionHandler> logger)
    {
        _voiceSessionService = voiceSessionService;
        _logger = logger;
    }

    /// <summary>
    /// Xu ly event block noi bo va ket thuc DM call neu hai user dang Ringing hoac Active.
    /// </summary>
    public async Task Handle(UserBlockedEvent notification, CancellationToken cancellationToken)
    {
        var endedCount = await _voiceSessionService.EndDirectCallsBetweenUsersAsync(
            notification.BlockerId,
            notification.BlockedUserId,
            cancellationToken);

        if (endedCount > 0)
        {
            _logger.LogInformation(
                "Ended {EndedCount} direct voice calls after UserBlockedEvent. BlockerId={BlockerId}; BlockedUserId={BlockedUserId}",
                endedCount,
                notification.BlockerId,
                notification.BlockedUserId);
        }
    }
}
