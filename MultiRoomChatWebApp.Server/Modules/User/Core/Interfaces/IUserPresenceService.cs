using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

/// <summary>
/// Service quản lý phần presence persist trong User module và snapshot presence được phép hiển thị.
/// </summary>
public interface IUserPresenceService
{
    /// <summary>
    /// Ghi mốc LastSeenAt khi user chuyển sang offline thật sự.
    /// </summary>
    Task MarkOfflineAsync(Guid userId, DateTime lastSeenAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy snapshot online/lastSeen của friends mà current user được phép nhìn thấy.
    /// </summary>
    Task<IReadOnlyCollection<PresenceDto>> GetFriendsPresenceAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
