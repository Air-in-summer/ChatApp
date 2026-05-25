namespace MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

/// <summary>
/// Service doc relationship graph de cac module khac check friend/block/presence audience.
/// </summary>
public interface IUserRelationshipGraphService
{
    /// <summary>
    /// Kiem tra hai user co dang la ban be hay khong.
    /// </summary>
    Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiem tra co bat ky quan he block nao giua hai user hay khong.
    /// </summary>
    Task<bool> HasBlockBetweenAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lay danh sach user id la ban be cua user hien tai.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetFriendIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lay danh sach user id co block hai chieu voi user hien tai.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetBlockedOrBlockingUserIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lay audience duoc phep nhan event presence cua user hien tai.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiem tra viewer co duoc thay online/lastSeen cua target hay khong.
    /// </summary>
    Task<bool> CanSeePresenceAsync(Guid viewerId, Guid targetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiem tra sender co duoc DM target hay khong.
    /// </summary>
    Task<bool> CanDirectMessageAsync(Guid senderId, Guid targetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiem tra caller co duoc goi DM voice toi callee hay khong.
    /// </summary>
    Task<bool> CanStartVoiceCallAsync(Guid callerId, Guid calleeId, CancellationToken cancellationToken = default);
}
