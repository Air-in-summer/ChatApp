namespace MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;

/// <summary>
/// DTO gửi yêu cầu thêm thành viên vào một phòng chat trong Group.
/// </summary>
public class AddRoomMembersRequest
{
    /// <summary>
    /// Danh sách ID của các User cần thêm vào phòng.
    /// </summary>
    public List<Guid> UserIds { get; set; } = new();
}
