using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

/// <summary>
/// Quan he chan mot chieu: Blocker chan Blocked.
/// </summary>
public class UserBlock
{
    public Guid BlockerId { get; set; }
    public virtual AppUser Blocker { get; set; } = null!;

    public Guid BlockedId { get; set; }
    public virtual AppUser Blocked { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
