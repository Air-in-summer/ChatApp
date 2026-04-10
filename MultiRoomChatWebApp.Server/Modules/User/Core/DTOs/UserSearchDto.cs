namespace MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

/// <summary>
/// DTO nhẹ dùng để trả về kết quả tìm kiếm người dùng.
/// Chứa đủ thông tin để FE hiển thị theo dạng: DisplayName (@Username)
/// </summary>
public class UserSearchDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
