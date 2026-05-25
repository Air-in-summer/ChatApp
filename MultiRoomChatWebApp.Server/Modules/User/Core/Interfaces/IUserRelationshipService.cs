using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

/// <summary>
/// Service quan ly du lieu relationship hien thi cho user.
/// Phase 1 chi can cac query doc nen tang; command friend/block se lam o phase API.
/// </summary>
public interface IUserRelationshipService
{
    /// <summary>
    /// Lay danh sach ban be cua user hien tai.
    /// </summary>
    Task<IReadOnlyCollection<FriendDto>> GetFriendsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lay danh sach loi moi ket ban ma user hien tai da nhan.
    /// </summary>
    Task<IReadOnlyCollection<FriendRequestDto>> GetIncomingFriendRequestsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lay danh sach loi moi ket ban ma user hien tai da gui.
    /// </summary>
    Task<IReadOnlyCollection<FriendRequestDto>> GetOutgoingFriendRequestsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lay danh sach user bi current user chan.
    /// </summary>
    Task<IReadOnlyCollection<BlockedUserDto>> GetBlockedUsersAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tao loi moi ket ban hoac accept request nguoc chieu dang pending.
    /// </summary>
    Task<FriendRequestDto> CreateFriendRequestAsync(Guid requesterId, CreateFriendRequestRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chap nhan loi moi ket ban ma current user la receiver.
    /// </summary>
    Task<FriendRequestDto> AcceptFriendRequestAsync(Guid receiverId, Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tu choi loi moi ket ban ma current user la receiver.
    /// </summary>
    Task<FriendRequestDto> DeclineFriendRequestAsync(Guid receiverId, Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Huy loi moi ket ban ma current user da gui.
    /// </summary>
    Task<FriendRequestDto> CancelFriendRequestAsync(Guid requesterId, Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoa quan he ban be giua current user va target user.
    /// </summary>
    Task RemoveFriendAsync(Guid currentUserId, Guid friendUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chan target user, dong thoi xoa friendship va pending requests hai chieu neu co.
    /// </summary>
    Task<BlockedUserDto> BlockUserAsync(Guid blockerId, BlockUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bo chan target user. Khong khoi phuc friendship cu.
    /// </summary>
    Task UnblockUserAsync(Guid blockerId, Guid blockedUserId, CancellationToken cancellationToken = default);
}
