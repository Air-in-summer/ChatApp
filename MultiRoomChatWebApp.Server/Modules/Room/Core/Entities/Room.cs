using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.Room.Core.Entities;

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid? GroupId { get; set; } // Future integration with Groups
    
    public RoomType Type { get; set; }
    
    public string? Name { get; set; }
    
    public bool IsPrivate { get; set; } = false;
    
    public int? MaxMembers { get; set; }
    
    public Guid CreatedBy { get; set; }
    public virtual AppUser Creator { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<RoomMember> Members { get; set; } = new List<RoomMember>();
}
