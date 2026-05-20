namespace MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

/// <summary>
/// DTO profile ca nhan cua user dang dang nhap.
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
/// Request cap nhat profile ca nhan trong scope core.
/// </summary>
public class UpdateUserProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// Request doi mat khau cho tai khoan password local.
/// </summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
