using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

/// <summary>
/// Trang thai presence duoc persist lau dai cho user.
/// Realtime online/offline van nam trong Redis, bang nay chi giu moc LastSeenAt.
/// </summary>
public class UserPresenceState
{
    public Guid UserId { get; set; }
    public virtual AppUser User { get; set; } = null!;

    public DateTime? LastSeenAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
