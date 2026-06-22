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
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// DTO trả về kết quả tìm kiếm người dùng theo trang.
/// </summary>
public class UserSearchResponseDto
{
    public IReadOnlyList<UserSearchDto> Items { get; set; } = Array.Empty<UserSearchDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
