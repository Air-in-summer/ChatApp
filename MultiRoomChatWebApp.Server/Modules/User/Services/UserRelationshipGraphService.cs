using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.User.Services;

/// <summary>
/// Service doc relationship graph de check friend/block va tinh audience presence.
/// </summary>
public class UserRelationshipGraphService : IUserRelationshipGraphService
{
    private readonly AppDbContext _dbContext;

    public UserRelationshipGraphService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Kiem tra hai user co dang la ban be hay khong.
    /// </summary>
    public async Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken = default)
    {
        if (userId == otherUserId)
            return false;

        var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);

        return await _dbContext.Friendships
            .AsNoTracking()
            .AnyAsync(friendship =>
                friendship.UserAId == userAId &&
                friendship.UserBId == userBId,
                cancellationToken);
    }

    /// <summary>
    /// Kiem tra co bat ky quan he block nao giua hai user hay khong.
    /// </summary>
    public async Task<bool> HasBlockBetweenAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken = default)
    {
        if (userId == otherUserId)
            return false;

        return await _dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(block =>
                (block.BlockerId == userId && block.BlockedId == otherUserId) ||
                (block.BlockerId == otherUserId && block.BlockedId == userId),
                cancellationToken);
    }

    /// <summary>
    /// Lay danh sach user id la ban be cua user hien tai.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> GetFriendIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userBIds = _dbContext.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.UserAId == userId)
            .Select(friendship => friendship.UserBId);

        var userAIds = _dbContext.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.UserBId == userId)
            .Select(friendship => friendship.UserAId);

        return await userBIds
            .Concat(userAIds)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lay danh sach user id co block hai chieu voi user hien tai.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> GetBlockedOrBlockingUserIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var blockedIds = _dbContext.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockerId == userId)
            .Select(block => block.BlockedId);

        var blockingIds = _dbContext.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockedId == userId)
            .Select(block => block.BlockerId);

        return await blockedIds
            .Concat(blockingIds)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lay audience duoc phep nhan event presence cua user hien tai.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var friendIds = await GetFriendIdsAsync(userId, cancellationToken);
        if (friendIds.Count == 0)
            return Array.Empty<Guid>();

        var blockedOrBlockingIds = await GetBlockedOrBlockingUserIdsAsync(userId, cancellationToken);
        if (blockedOrBlockingIds.Count == 0)
            return friendIds;

        var excludedIds = blockedOrBlockingIds.ToHashSet();
        return friendIds
            .Where(friendId => !excludedIds.Contains(friendId))
            .ToList();
    }

    /// <summary>
    /// Kiem tra viewer co duoc thay online/lastSeen cua target hay khong.
    /// </summary>
    public async Task<bool> CanSeePresenceAsync(Guid viewerId, Guid targetId, CancellationToken cancellationToken = default)
    {
        if (viewerId == targetId)
            return false;

        if (!await AreFriendsAsync(viewerId, targetId, cancellationToken))
            return false;

        return !await HasBlockBetweenAsync(viewerId, targetId, cancellationToken);
    }

    /// <summary>
    /// Kiem tra sender co duoc DM target hay khong.
    /// </summary>
    public async Task<bool> CanDirectMessageAsync(Guid senderId, Guid targetId, CancellationToken cancellationToken = default)
    {
        if (senderId == targetId)
            return false;

        return !await HasBlockBetweenAsync(senderId, targetId, cancellationToken);
    }

    /// <summary>
    /// Kiem tra caller co duoc goi DM voice toi callee hay khong.
    /// </summary>
    public async Task<bool> CanStartVoiceCallAsync(Guid callerId, Guid calleeId, CancellationToken cancellationToken = default)
    {
        if (callerId == calleeId)
            return false;

        return !await HasBlockBetweenAsync(callerId, calleeId, cancellationToken);
    }

    private static (Guid UserAId, Guid UserBId) NormalizeFriendPair(Guid firstUserId, Guid secondUserId)
    {
        return firstUserId.CompareTo(secondUserId) < 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);
    }
}
