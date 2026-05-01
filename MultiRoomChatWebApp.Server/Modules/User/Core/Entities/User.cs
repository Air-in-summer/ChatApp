namespace MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Username { get; set; } = string.Empty;
    
    public string DisplayName { get; set; } = string.Empty;
    
    public string Email { get; set; } = string.Empty;
    
    public string PasswordHash { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities.RefreshToken> RefreshTokens { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities.RefreshToken>();
    
    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.Room> CreatedRooms { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.Room>();
    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.RoomMember> RoomMemberships { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.RoomMember>();
    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.GroupMember> GroupMemberships { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.GroupMember>();
}
