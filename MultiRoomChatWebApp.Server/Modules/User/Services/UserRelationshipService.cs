using Microsoft.EntityFrameworkCore;
using MediatR;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.User.Core.Cache;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.User.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.User.Core.Events;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.User.Services;

/// <summary>
/// Service doc va cap nhat friends/requests/blocks cho UI relationship.
/// </summary>
public class UserRelationshipService : IUserRelationshipService
{
    private static readonly TimeSpan DirectInteractionBlockCacheTtl = TimeSpan.FromDays(1);

    private readonly AppDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMediator _mediator;
    private readonly ILogger<UserRelationshipService> _logger;

    public UserRelationshipService(
        AppDbContext dbContext,
        IConnectionMultiplexer redis,
        IMediator mediator,
        ILogger<UserRelationshipService> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Lay danh sach ban be cua user hien tai.
    /// </summary>
    public async Task<IReadOnlyCollection<FriendDto>> GetFriendsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var friendsFromUserA = _dbContext.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.UserAId == userId && friendship.UserB.IsActive)
            .Select(friendship => new FriendDto
            {
                User = new UserRelationshipProfileDto
                {
                    Id = friendship.UserB.Id,
                    Username = friendship.UserB.Username,
                    DisplayName = friendship.UserB.DisplayName,
                    AvatarUrl = friendship.UserB.AvatarUrl
                },
                FriendsSince = friendship.CreatedAt
            });

        var friendsFromUserB = _dbContext.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.UserBId == userId && friendship.UserA.IsActive)
            .Select(friendship => new FriendDto
            {
                User = new UserRelationshipProfileDto
                {
                    Id = friendship.UserA.Id,
                    Username = friendship.UserA.Username,
                    DisplayName = friendship.UserA.DisplayName,
                    AvatarUrl = friendship.UserA.AvatarUrl
                },
                FriendsSince = friendship.CreatedAt
            });

        return await friendsFromUserA
            .Concat(friendsFromUserB)
            .OrderBy(friend => friend.User.DisplayName)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lay danh sach loi moi ket ban ma user hien tai da nhan.
    /// </summary>
    public async Task<IReadOnlyCollection<FriendRequestDto>> GetIncomingFriendRequestsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FriendRequests
            .AsNoTracking()
            .Where(request =>
                request.ReceiverId == userId &&
                request.Status == FriendRequestStatus.Pending &&
                request.Requester.IsActive)
            .OrderByDescending(request => request.CreatedAt)
            .Select(request => new FriendRequestDto
            {
                Id = request.Id,
                Requester = new UserRelationshipProfileDto
                {
                    Id = request.Requester.Id,
                    Username = request.Requester.Username,
                    DisplayName = request.Requester.DisplayName,
                    AvatarUrl = request.Requester.AvatarUrl
                },
                Receiver = new UserRelationshipProfileDto
                {
                    Id = request.Receiver.Id,
                    Username = request.Receiver.Username,
                    DisplayName = request.Receiver.DisplayName,
                    AvatarUrl = request.Receiver.AvatarUrl
                },
                Status = request.Status,
                CreatedAt = request.CreatedAt,
                RespondedAt = request.RespondedAt,
                CanceledAt = request.CanceledAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lay danh sach loi moi ket ban ma user hien tai da gui.
    /// </summary>
    public async Task<IReadOnlyCollection<FriendRequestDto>> GetOutgoingFriendRequestsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FriendRequests
            .AsNoTracking()
            .Where(request =>
                request.RequesterId == userId &&
                request.Status == FriendRequestStatus.Pending &&
                request.Receiver.IsActive)
            .OrderByDescending(request => request.CreatedAt)
            .Select(request => new FriendRequestDto
            {
                Id = request.Id,
                Requester = new UserRelationshipProfileDto
                {
                    Id = request.Requester.Id,
                    Username = request.Requester.Username,
                    DisplayName = request.Requester.DisplayName,
                    AvatarUrl = request.Requester.AvatarUrl
                },
                Receiver = new UserRelationshipProfileDto
                {
                    Id = request.Receiver.Id,
                    Username = request.Receiver.Username,
                    DisplayName = request.Receiver.DisplayName,
                    AvatarUrl = request.Receiver.AvatarUrl
                },
                Status = request.Status,
                CreatedAt = request.CreatedAt,
                RespondedAt = request.RespondedAt,
                CanceledAt = request.CanceledAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lay danh sach user bi current user chan.
    /// </summary>
    public async Task<IReadOnlyCollection<BlockedUserDto>> GetBlockedUsersAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockerId == userId && block.Blocked.IsActive)
            .OrderByDescending(block => block.CreatedAt)
            .Select(block => new BlockedUserDto
            {
                User = new UserRelationshipProfileDto
                {
                    Id = block.Blocked.Id,
                    Username = block.Blocked.Username,
                    DisplayName = block.Blocked.DisplayName,
                    AvatarUrl = block.Blocked.AvatarUrl
                },
                BlockedAt = block.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Tao loi moi ket ban hoac accept request nguoc chieu dang pending.
    /// </summary>
    /// <remarks>
    /// Luong xu ly:
    /// 1. Validate self-target, target active, friendship va block hai chieu.
    /// 2. Chan duplicate pending cung chieu.
    /// 3. Neu co pending nguoc chieu thi accept request do de tranh hai request doi xung.
    /// 4. Neu khong co pending nguoc chieu thi tao request moi.
    /// </remarks>
    public async Task<FriendRequestDto> CreateFriendRequestAsync(
        Guid requesterId,
        CreateFriendRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        var receiverId = request.ReceiverId;
        if (requesterId == receiverId)
            throw ApiException.BadRequest("invalid_friend_request", "Không thể gửi lời mời kết bạn cho chính mình.");

        await EnsureActiveUserExistsAsync(receiverId, cancellationToken);
        await EnsureNoBlockBetweenAsync(requesterId, receiverId, cancellationToken);

        if (await AreFriendsAsync(requesterId, receiverId, cancellationToken))
            throw ApiException.Conflict("already_friends", "Hai tài khoản đã là bạn bè.");

        var existingPending = await _dbContext.FriendRequests
            .FirstOrDefaultAsync(friendRequest =>
                friendRequest.RequesterId == requesterId &&
                friendRequest.ReceiverId == receiverId &&
                friendRequest.Status == FriendRequestStatus.Pending,
                cancellationToken);

        if (existingPending != null)
            throw ApiException.Conflict("friend_request_pending", "Lời mời kết bạn đã được gửi trước đó.");

        var reversePending = await _dbContext.FriendRequests
            .FirstOrDefaultAsync(friendRequest =>
                friendRequest.RequesterId == receiverId &&
                friendRequest.ReceiverId == requesterId &&
                friendRequest.Status == FriendRequestStatus.Pending,
                cancellationToken);

        if (reversePending != null)
            return await AcceptFriendRequestAsync(requesterId, reversePending.Id, cancellationToken);

        var newRequest = new FriendRequest
        {
            RequesterId = requesterId,
            ReceiverId = receiverId,
            Status = FriendRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.FriendRequests.Add(newRequest);
        await _dbContext.SaveChangesAsync(cancellationToken);

        LogRelationshipAction("FriendRequestCreated", requesterId, receiverId, newRequest.Id);
        return await GetFriendRequestDtoAsync(newRequest.Id, cancellationToken);
    }

    /// <summary>
    /// Chap nhan loi moi ket ban ma current user la receiver.
    /// </summary>
    /// <remarks>
    /// Luong xu ly:
    /// 1. Lay pending request va validate current user la receiver.
    /// 2. Check block hai chieu truoc khi tao friendship.
    /// 3. Tao friendship theo normalized pair neu chua ton tai.
    /// 4. Mark request accepted va cancel pending request khac giua hai user.
    /// </remarks>
    public async Task<FriendRequestDto> AcceptFriendRequestAsync(
        Guid receiverId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var friendRequest = await GetOwnedPendingRequestAsync(requestId, receiverId, isReceiver: true, cancellationToken);

        await EnsureNoBlockBetweenAsync(receiverId, friendRequest.RequesterId, cancellationToken);

        var now = DateTime.UtcNow;
        var (userAId, userBId) = NormalizeFriendPair(friendRequest.RequesterId, friendRequest.ReceiverId);
        var friendshipExists = await _dbContext.Friendships
            .AnyAsync(friendship =>
                friendship.UserAId == userAId &&
                friendship.UserBId == userBId,
                cancellationToken);

        if (!friendshipExists)
        {
            _dbContext.Friendships.Add(new Friendship
            {
                UserAId = userAId,
                UserBId = userBId,
                CreatedAt = now
            });
        }

        friendRequest.Status = FriendRequestStatus.Accepted;
        friendRequest.RespondedAt = now;

        await CancelPendingRequestsBetweenUsersAsync(
            friendRequest.RequesterId,
            friendRequest.ReceiverId,
            now,
            excludedRequestId: friendRequest.Id,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogRelationshipAction("FriendRequestAccepted", receiverId, friendRequest.RequesterId, friendRequest.Id);
        return await GetFriendRequestDtoAsync(friendRequest.Id, cancellationToken);
    }

    /// <summary>
    /// Tu choi loi moi ket ban ma current user la receiver.
    /// </summary>
    public async Task<FriendRequestDto> DeclineFriendRequestAsync(
        Guid receiverId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var friendRequest = await GetOwnedPendingRequestAsync(requestId, receiverId, isReceiver: true, cancellationToken);

        friendRequest.Status = FriendRequestStatus.Declined;
        friendRequest.RespondedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        LogRelationshipAction("FriendRequestDeclined", receiverId, friendRequest.RequesterId, friendRequest.Id);
        return await GetFriendRequestDtoAsync(friendRequest.Id, cancellationToken);
    }

    /// <summary>
    /// Huy loi moi ket ban ma current user da gui.
    /// </summary>
    public async Task<FriendRequestDto> CancelFriendRequestAsync(
        Guid requesterId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var friendRequest = await GetOwnedPendingRequestAsync(requestId, requesterId, isReceiver: false, cancellationToken);

        friendRequest.Status = FriendRequestStatus.Canceled;
        friendRequest.CanceledAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        LogRelationshipAction("FriendRequestCanceled", requesterId, friendRequest.ReceiverId, friendRequest.Id);
        return await GetFriendRequestDtoAsync(friendRequest.Id, cancellationToken);
    }

    /// <summary>
    /// Xoa quan he ban be giua current user va target user.
    /// </summary>
    public async Task RemoveFriendAsync(
        Guid currentUserId,
        Guid friendUserId,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId == friendUserId)
            throw ApiException.BadRequest("invalid_friend_target", "Không thể xóa chính mình khỏi danh sách bạn bè.");

        var (userAId, userBId) = NormalizeFriendPair(currentUserId, friendUserId);
        var friendship = await _dbContext.Friendships
            .FirstOrDefaultAsync(entity =>
                entity.UserAId == userAId &&
                entity.UserBId == userBId,
                cancellationToken);

        if (friendship == null)
            throw ApiException.NotFound("friendship_not_found", "Không tìm thấy quan hệ bạn bè.");

        _dbContext.Friendships.Remove(friendship);
        await _dbContext.SaveChangesAsync(cancellationToken);

        LogRelationshipAction("FriendRemoved", currentUserId, friendUserId);
    }

    /// <summary>
    /// Chan target user, dong thoi xoa friendship va pending requests hai chieu neu co.
    /// </summary>
    /// <remarks>
    /// Luong xu ly:
    /// 1. Validate self-target va target active.
    /// 2. Tao block neu chua ton tai.
    /// 3. Remove friendship neu hai user dang la ban.
    /// 4. Cancel tat ca pending friend requests hai chieu.
    /// </remarks>
    public async Task<BlockedUserDto> BlockUserAsync(
        Guid blockerId,
        BlockUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var blockedUserId = request.BlockedUserId;
        if (blockerId == blockedUserId)
            throw ApiException.BadRequest("invalid_block_target", "Không thể chặn chính mình.");

        await EnsureActiveUserExistsAsync(blockedUserId, cancellationToken);

        var existingBlock = await _dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(block =>
                block.BlockerId == blockerId &&
                block.BlockedId == blockedUserId,
                cancellationToken);

        if (existingBlock)
        {
            await CacheDirectInteractionBlockedAsync(blockerId, blockedUserId);
            await _mediator.Publish(new UserBlockedEvent(blockerId, blockedUserId), cancellationToken);
            return await GetBlockedUserDtoAsync(blockerId, blockedUserId, cancellationToken);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;

        _dbContext.UserBlocks.Add(new UserBlock
        {
            BlockerId = blockerId,
            BlockedId = blockedUserId,
            CreatedAt = now
        });

        var (userAId, userBId) = NormalizeFriendPair(blockerId, blockedUserId);
        var friendship = await _dbContext.Friendships
            .FirstOrDefaultAsync(entity =>
                entity.UserAId == userAId &&
                entity.UserBId == userBId,
                cancellationToken);

        if (friendship != null)
            _dbContext.Friendships.Remove(friendship);

        await CancelPendingRequestsBetweenUsersAsync(
            blockerId,
            blockedUserId,
            now,
            excludedRequestId: null,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await CacheDirectInteractionBlockedAsync(blockerId, blockedUserId);
        await _mediator.Publish(new UserBlockedEvent(blockerId, blockedUserId), cancellationToken);

        LogRelationshipAction("UserBlocked", blockerId, blockedUserId);
        return await GetBlockedUserDtoAsync(blockerId, blockedUserId, cancellationToken);
    }

    /// <summary>
    /// Bo chan target user. Khong khoi phuc friendship cu.
    /// </summary>
    public async Task UnblockUserAsync(
        Guid blockerId,
        Guid blockedUserId,
        CancellationToken cancellationToken = default)
    {
        if (blockerId == blockedUserId)
            throw ApiException.BadRequest("invalid_unblock_target", "Không thể bỏ chặn chính mình.");

        var block = await _dbContext.UserBlocks
            .FirstOrDefaultAsync(entity =>
                entity.BlockerId == blockerId &&
                entity.BlockedId == blockedUserId,
                cancellationToken);

        if (block == null)
            return;

        var reverseBlockExists = await _dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(entity =>
                entity.BlockerId == blockedUserId &&
                entity.BlockedId == blockerId,
                cancellationToken);

        _dbContext.UserBlocks.Remove(block);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await RefreshDirectInteractionCacheAfterUnblockAsync(
            blockerId,
            blockedUserId,
            reverseBlockExists);

        LogRelationshipAction("UserUnblocked", blockerId, blockedUserId);
    }

    private async Task CacheDirectInteractionBlockedAsync(Guid firstUserId, Guid secondUserId)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(
            UserRelationshipCacheKeys.BlockBetween(firstUserId, secondUserId),
            "1",
            DirectInteractionBlockCacheTtl);
        await db.KeyDeleteAsync(UserRelationshipCacheKeys.AllowBetween(firstUserId, secondUserId));
    }

    private async Task RefreshDirectInteractionCacheAfterUnblockAsync(
        Guid firstUserId,
        Guid secondUserId,
        bool reverseBlockExists)
    {
        var db = _redis.GetDatabase();
        var blockKey = UserRelationshipCacheKeys.BlockBetween(firstUserId, secondUserId);
        var allowKey = UserRelationshipCacheKeys.AllowBetween(firstUserId, secondUserId);

        await db.KeyDeleteAsync(allowKey);

        if (reverseBlockExists)
        {
            await db.StringSetAsync(blockKey, "1", DirectInteractionBlockCacheTtl);
            return;
        }

        await db.KeyDeleteAsync(blockKey);
    }

    private async Task EnsureActiveUserExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken);

        if (!userExists)
            throw ApiException.NotFound("user_not_found", "Không tìm thấy tài khoản.");
    }

    private async Task EnsureNoBlockBetweenAsync(Guid currentUserId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var currentBlockedTarget = await _dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(block =>
                block.BlockerId == currentUserId &&
                block.BlockedId == targetUserId,
                cancellationToken);

        if (currentBlockedTarget)
            throw ApiException.Conflict("user_blocked_by_current_user", "Bạn đã chặn người này.");

        var targetBlockedCurrent = await _dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(block =>
                block.BlockerId == targetUserId &&
                block.BlockedId == currentUserId,
                cancellationToken);

        if (targetBlockedCurrent)
            throw ApiException.Conflict("relationship_action_not_allowed", "Không thể thực hiện hành động này.");
    }

    private async Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken)
    {
        var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);
        return await _dbContext.Friendships
            .AsNoTracking()
            .AnyAsync(friendship =>
                friendship.UserAId == userAId &&
                friendship.UserBId == userBId,
                cancellationToken);
    }

    private async Task<FriendRequest> GetOwnedPendingRequestAsync(
        Guid requestId,
        Guid currentUserId,
        bool isReceiver,
        CancellationToken cancellationToken)
    {
        var friendRequest = await _dbContext.FriendRequests
            .FirstOrDefaultAsync(request =>
                request.Id == requestId &&
                request.Status == FriendRequestStatus.Pending,
                cancellationToken);

        if (friendRequest == null)
            throw ApiException.NotFound("friend_request_not_found", "Không tìm thấy lời mời kết bạn.");

        var ownsRequest = isReceiver
            ? friendRequest.ReceiverId == currentUserId
            : friendRequest.RequesterId == currentUserId;

        if (!ownsRequest)
            throw ApiException.NotFound("friend_request_not_found", "Không tìm thấy lời mời kết bạn.");

        return friendRequest;
    }

    private async Task CancelPendingRequestsBetweenUsersAsync(
        Guid firstUserId,
        Guid secondUserId,
        DateTime now,
        Guid? excludedRequestId,
        CancellationToken cancellationToken)
    {
        var pendingRequests = await _dbContext.FriendRequests
            .Where(request =>
                request.Status == FriendRequestStatus.Pending &&
                (!excludedRequestId.HasValue || request.Id != excludedRequestId.Value) &&
                ((request.RequesterId == firstUserId && request.ReceiverId == secondUserId) ||
                 (request.RequesterId == secondUserId && request.ReceiverId == firstUserId)))
            .ToListAsync(cancellationToken);

        foreach (var pendingRequest in pendingRequests)
        {
            pendingRequest.Status = FriendRequestStatus.Canceled;
            pendingRequest.CanceledAt = now;
        }
    }

    private async Task<FriendRequestDto> GetFriendRequestDtoAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var dto = await _dbContext.FriendRequests
            .AsNoTracking()
            .Where(request => request.Id == requestId)
            .Select(request => new FriendRequestDto
            {
                Id = request.Id,
                Requester = new UserRelationshipProfileDto
                {
                    Id = request.Requester.Id,
                    Username = request.Requester.Username,
                    DisplayName = request.Requester.DisplayName,
                    AvatarUrl = request.Requester.AvatarUrl
                },
                Receiver = new UserRelationshipProfileDto
                {
                    Id = request.Receiver.Id,
                    Username = request.Receiver.Username,
                    DisplayName = request.Receiver.DisplayName,
                    AvatarUrl = request.Receiver.AvatarUrl
                },
                Status = request.Status,
                CreatedAt = request.CreatedAt,
                RespondedAt = request.RespondedAt,
                CanceledAt = request.CanceledAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return dto ?? throw ApiException.NotFound("friend_request_not_found", "Không tìm thấy lời mời kết bạn.");
    }

    private async Task<BlockedUserDto> GetBlockedUserDtoAsync(
        Guid blockerId,
        Guid blockedUserId,
        CancellationToken cancellationToken)
    {
        var dto = await _dbContext.UserBlocks
            .AsNoTracking()
            .Where(block =>
                block.BlockerId == blockerId &&
                block.BlockedId == blockedUserId)
            .Select(block => new BlockedUserDto
            {
                User = new UserRelationshipProfileDto
                {
                    Id = block.Blocked.Id,
                    Username = block.Blocked.Username,
                    DisplayName = block.Blocked.DisplayName,
                    AvatarUrl = block.Blocked.AvatarUrl
                },
                BlockedAt = block.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return dto ?? throw ApiException.NotFound("block_not_found", "Không tìm thấy trạng thái chặn.");
    }

    private void LogRelationshipAction(string action, Guid actorUserId, Guid targetUserId, Guid? requestId = null)
    {
        _logger.LogInformation(
            "Relationship action completed. Action={Action}; ActorUserId={ActorUserId}; TargetUserId={TargetUserId}; RequestId={RequestId}",
            action,
            actorUserId,
            targetUserId,
            requestId);
    }

    private static (Guid UserAId, Guid UserBId) NormalizeFriendPair(Guid firstUserId, Guid secondUserId)
    {
        return firstUserId.CompareTo(secondUserId) < 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);
    }
}
