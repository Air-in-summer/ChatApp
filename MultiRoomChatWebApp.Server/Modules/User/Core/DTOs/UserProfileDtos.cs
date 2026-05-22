namespace MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

/// <summary>
/// DTO profile cá nhân của user đang đăng nhập.
/// </summary>
public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// Request cập nhật profile cá nhân trong scope core.
/// </summary>
public class UpdateUserProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// Request đổi mật khẩu cho tài khoản password local.
/// </summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
