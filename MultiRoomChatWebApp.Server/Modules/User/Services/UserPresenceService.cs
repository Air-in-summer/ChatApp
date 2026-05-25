using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.User.Services;

/// <summary>
/// Service đọc/ghi presence state bền vững cho User module.
/// </summary>
public class UserPresenceService : IUserPresenceService
{
    private readonly AppDbContext _dbContext;
    private readonly IUserRelationshipGraphService _relationshipGraphService;
    private readonly IPresenceTracker _presenceTracker;

    public UserPresenceService(
        AppDbContext dbContext,
        IUserRelationshipGraphService relationshipGraphService,
        IPresenceTracker presenceTracker)
    {
        _dbContext = dbContext;
        _relationshipGraphService = relationshipGraphService;
        _presenceTracker = presenceTracker;
    }

    /// <summary>
    /// Upsert LastSeenAt khi user hết connection hợp lệ.
    /// </summary>
    public async Task MarkOfflineAsync(
        Guid userId,
        DateTime lastSeenAtUtc,
        CancellationToken cancellationToken = default)
    {
        var presenceState = await _dbContext.UserPresenceStates
            .FirstOrDefaultAsync(state => state.UserId == userId, cancellationToken);

        if (presenceState == null)
        {
            _dbContext.UserPresenceStates.Add(new UserPresenceState
            {
                UserId = userId,
                LastSeenAt = lastSeenAtUtc,
                UpdatedAt = lastSeenAtUtc
            });
        }
        else
        {
            presenceState.LastSeenAt = lastSeenAtUtc;
            presenceState.UpdatedAt = lastSeenAtUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Trả presence snapshot chỉ cho audience friends-only đã loại block hai chiều.
    /// </summary>
    public async Task<IReadOnlyCollection<PresenceDto>> GetFriendsPresenceAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var audienceIds = await _relationshipGraphService.GetPresenceAudienceAsync(
            currentUserId,
            cancellationToken);

        if (audienceIds.Count == 0)
        {
            return Array.Empty<PresenceDto>();
        }

        var onlineIds = (await _presenceTracker.GetOnlineUsersAsync(audienceIds))
            .ToHashSet();

        var lastSeenByUserId = await _dbContext.UserPresenceStates
            .AsNoTracking()
            .Where(state => audienceIds.Contains(state.UserId))
            .ToDictionaryAsync(
                state => state.UserId,
                state => state.LastSeenAt,
                cancellationToken);

        return audienceIds
            .Select(userId => new PresenceDto
            {
                UserId = userId,
                IsOnline = onlineIds.Contains(userId),
                LastSeenAt = lastSeenByUserId.GetValueOrDefault(userId)
            })
            .ToList();
    }
}
