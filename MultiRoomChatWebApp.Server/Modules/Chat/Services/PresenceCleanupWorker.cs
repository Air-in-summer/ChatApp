using Microsoft.AspNetCore.SignalR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Background worker dọn Redis presence connection hết hạn do tab crash/mất mạng không bắn disconnect sạch.
/// </summary>
public class PresenceCleanupWorker : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PresenceCleanupWorker> _logger;

    public PresenceCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PresenceCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Quét định kỳ tập user active/recent, update LastSeen và emit offline cho friends audience hợp lệ.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PresenceCleanupWorker started. CleanupInterval={CleanupIntervalSeconds}s",
            CleanupInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var presenceTracker = scope.ServiceProvider.GetRequiredService<IPresenceTracker>();
                var userPresenceService = scope.ServiceProvider.GetRequiredService<IUserPresenceService>();
                var relationshipGraphService = scope.ServiceProvider.GetRequiredService<IUserRelationshipGraphService>();
                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub, IChatClient>>();

                var offlineUserIds = await presenceTracker.CleanupExpiredConnectionsAsync(stoppingToken);
                foreach (var offlineUserId in offlineUserIds)
                {
                    await MarkOfflineAndNotifyAudienceAsync(
                        offlineUserId,
                        userPresenceService,
                        relationshipGraphService,
                        hubContext,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PresenceCleanupWorker failed while pruning expired connections.");
            }
        }
    }

    private static async Task MarkOfflineAndNotifyAudienceAsync(
        Guid offlineUserId,
        IUserPresenceService userPresenceService,
        IUserRelationshipGraphService relationshipGraphService,
        IHubContext<ChatHub, IChatClient> hubContext,
        CancellationToken cancellationToken)
    {
        await userPresenceService.MarkOfflineAsync(offlineUserId, DateTime.UtcNow, cancellationToken);

        var audienceIds = await relationshipGraphService.GetPresenceAudienceAsync(
            offlineUserId,
            cancellationToken);

        if (audienceIds.Count == 0)
        {
            return;
        }

        await hubContext.Clients
            .Users(audienceIds.Select(userId => userId.ToString()))
            .UserIsOffline(offlineUserId);
    }
}
