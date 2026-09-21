using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;

/// <summary>
/// Lưu thông tin liên kết tài khoản giữa User nội bộ và các nền tảng bên thứ ba (OAuth).
/// </summary>
public class ExternalLogin
{
    /// <summary>
    /// Khóa chính liên kết login.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Khóa ngoại trỏ về tài khoản người dùng nội bộ (Users).
    /// </summary>
    public Guid UserId { get; set; }
    public virtual AppUser User { get; set; } = null!;

    public string Provider { get; set; } = string.Empty;

    public string ProviderUserId { get; set; } = string.Empty;

    public string ProviderEmail { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
