using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

/// <summary>
/// Giao diện xử lý các nghiệp vụ xác thực người dùng cơ bản.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Đăng ký tài khoản người dùng mới.
    /// </summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    
    /// <summary>
    /// Xác thực thông tin đăng nhập bằng username/email và mật khẩu.
    /// </summary>
    Task<AuthResponse> LoginAsync(LoginRequest request);
    
    /// <summary>
    /// Xác thực người dùng qua các nhà cung cấp bên thứ 3 (Google, Facebook...).
    /// </summary>
    Task<ExternalLoginAuthResult> LoginWithExternalProviderAsync(ExternalLoginRequest request);
}
