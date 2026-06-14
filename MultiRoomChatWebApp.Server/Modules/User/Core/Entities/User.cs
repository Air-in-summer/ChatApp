namespace MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Username { get; set; } = string.Empty;
    
    public string DisplayName { get; set; } = string.Empty;
    
    public string Email { get; set; } = string.Empty;
    
    public string? PasswordHash { get; set; }

    public string? AvatarUrl { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities.ExternalLogin> ExternalLogins { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities.ExternalLogin>();
    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities.AuthSession> AuthSessions { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities.AuthSession>();
    
    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.Room> CreatedRooms { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.Room>();
    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.RoomMember> RoomMemberships { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.RoomMember>();
    public virtual ICollection<MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.GroupMember> GroupMemberships { get; set; } = new List<MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.GroupMember>();
}
