using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;

/// <summary>
/// DTO phối hợp thông tin Danh tính (User) và Bối cảnh (Role trong Group).
/// </summary>
public class GroupMemberDto
{
    public UserCacheDto Profile { get; set; } = null!;
    public GroupRole Role { get; set; }
}
