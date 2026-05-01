using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Entities;

/// <summary>
/// Bảng quan hệ N-N giữa User và Group.
/// </summary>
public class GroupMember
{
    public Guid GroupId { get; set; }
    public virtual Group Group { get; set; } = null!;
    
    public Guid UserId { get; set; }
    public virtual AppUser User { get; set; } = null!;
    
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    
    public GroupRole Role { get; set; } = GroupRole.Member;
}
