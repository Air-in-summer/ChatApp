using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Entities;

/// <summary>
/// Thực thể đại diện cho một Server (Group) chứa nhiều Channels (Rooms).
/// Hỗ trợ Soft Delete qua thuộc tính DeletedAt.
/// </summary>
public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    public string? IconUrl { get; set; }
    
    /// <summary>
    /// Mã mời duy nhất dùng để tạo Link tham gia Server.
    /// </summary>
    public string InviteCode { get; set; } = string.Empty;
    
    public Guid OwnerId { get; set; }
    public virtual AppUser Owner { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Ngày xóa mềm. Nếu có giá trị, Group này sẽ bị ẩn khỏi hệ thống.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    // Relationships
    public virtual ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public virtual ICollection<Room.Core.Entities.Room> Rooms { get; set; } = new List<Room.Core.Entities.Room>();
}
