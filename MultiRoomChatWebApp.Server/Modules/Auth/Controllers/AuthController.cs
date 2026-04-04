using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AuthLimit")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// [POST] /api/auth/register - Đăng ký tài khoản người dùng mới
    /// </summary>
    /// <param name="request">Bao gồm Username, DisplayName, Email, và Password</param>
    /// <returns>AuthResponse chứa Access Token và Refresh Token</returns>
    /// <remarks>
    /// Request:
    /// - Body: RegisterRequest { Username, DisplayName, Email, Password }
    /// 
    /// Response Success (200):
    /// {
    ///   "accessToken": "eyJ...",
    ///   "refreshToken": "base64...",
    ///   "userId": "guid...",
    ///   "username": "alice",
    ///   "displayName": "Alice"
    /// }
    /// 
    /// Response Error:
    /// - 400: Email hoặc Username đã tồn tại trong hệ thống.
    /// 
    /// Side effects:
    /// - Tạo user mới trong DB và băm mật khẩu bằng BCrypt.
    /// - Phát sinh một Refresh Token lưu vào CSDL.
    /// </remarks>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await _authService.RegisterAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// [POST] /api/auth/login - Đăng nhập vào hệ thống
    /// </summary>
    /// <param name="request">Bao gồm Email và Password</param>
    /// <returns>AuthResponse chứa Access Token và Refresh Token</returns>
    /// <remarks>
    /// Request:
    /// - Body: LoginRequest { Email, Password }
    /// 
    /// Response Success (200):
    /// Trả về accessToken (15m) và refreshToken (7 ngày)
    /// 
    /// Response Error:
    /// - 401: Sai email, sai mật khẩu, hoặc tài khoản đã bị khóa (IsActive = false).
    /// </remarks>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// [POST] /api/auth/refresh - Cấp lại Access Token mới bằng Refresh Token
    /// </summary>
    /// <param name="request">Bao gồm Refresh Token cũ cần gia hạn</param>
    /// <returns>AuthResponse với các token mới nhất (Rotation)</returns>
    /// <remarks>
    /// Request:
    /// - Body: RefreshRequest { RefreshToken }
    /// 
    /// Response Success (200):
    /// Trả về cặp accessToken và refreshToken hoàn toàn mới.
    /// 
    /// Response Error:
    /// - 401: Refresh Token gửi lên không hợp lệ, đã bị thu hồi hoặc hết hạn.
    /// 
    /// Side effects:
    /// - Token cũ sẽ bị đánh dấu IsRevoked = true để tránh dùng lại.
    /// - Tự động xóa các token rác của cùng user nếu có cặn lại (Lazy Cleanup).
    /// </remarks>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var response = await _authService.RefreshAsync(request.RefreshToken);
        return Ok(response);
    }

    /// <summary>
    /// [POST] /api/auth/logout - Khóa phiên đăng nhập hiện tại
    /// </summary>
    /// <param name="request">Bao gồm Refresh Token cần thu hồi</param>
    /// <returns>Trả về kết quả 200 OK khi đăng xuất xong</returns>
    /// <remarks>
    /// Request:
    /// - Body: LogoutRequest { RefreshToken }
    /// 
    /// Side effects:
    /// - Refresh Token bị đánh dấu IsRevoked = true trong DB, ngăn cản việc refresh mới.
    /// </remarks>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        await _authService.LogoutAsync(request.RefreshToken);
        return Ok();
    }
}
