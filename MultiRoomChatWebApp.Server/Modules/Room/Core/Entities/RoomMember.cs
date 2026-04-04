using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.Room.Core.Entities;

public class RoomMember
{
    public Guid RoomId { get; set; }
    public virtual Room Room { get; set; } = null!;

    public Guid UserId { get; set; }
    public virtual AppUser User { get; set; } = null!;

    public RoomRole Role { get; set; } = RoomRole.Member;
    
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
