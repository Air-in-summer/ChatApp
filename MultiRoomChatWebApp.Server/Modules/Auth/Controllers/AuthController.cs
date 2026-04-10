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
    /// Helper: Đặt Refresh Token vào HttpOnly Cookie trên Response.
    /// </summary>
    /// <param name="refreshToken">Giá trị Refresh Token cần lưu vào Cookie</param>
    /// <remarks>
    /// Cấu hình Cookie đảm bảo tuyệt đối an toàn:
    /// - HttpOnly = true : JavaScript không thể đọc được → chống XSS ăn cắp token.
    /// - Secure = true   : Chỉ gửi qua HTTPS → chống nghe lén trên đường truyền.
    /// - SameSite = Lax  : Không gửi trong POST/PUT/DELETE cross-site → chống CSRF cơ bản.
    ///                     Cho phép gửi khi người dùng click link navigation → UX tốt hơn.
    /// - MaxAge = 7 ngày : Khớp với thời hạn RefreshToken trong DB.
    /// </remarks>
    private void SetRefreshTokenCookie(string refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure   = true,
            SameSite = SameSiteMode.Lax,
            MaxAge   = TimeSpan.FromDays(7)
        };

        Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
    }

    /// <summary>
    /// [POST] /api/auth/register - Đăng ký tài khoản người dùng mới
    /// </summary>
    /// <param name="request">Bao gồm Username, DisplayName, Email, và Password</param>
    /// <returns>AuthClientResponse chứa Access Token và thông tin user (không có Refresh Token)</returns>
    /// <remarks>
    /// Request:
    /// - Body: RegisterRequest { Username, DisplayName, Email, Password }
    /// 
    /// Response Success (200):
    /// {
    ///   "accessToken": "eyJ...",
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
    /// - Phát sinh một Refresh Token lưu vào CSDL và gắn vào HttpOnly Cookie.
    /// </remarks>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);

        // Lưu RefreshToken vào HttpOnly Cookie (ẩn khỏi JavaScript)
        SetRefreshTokenCookie(result.RefreshToken);

        // Chỉ trả về AccessToken + User info, KHÔNG trả RefreshToken trong body
        return Ok(new AuthClientResponse(
            result.AccessToken,
            result.UserId,
            result.Username,
            result.DisplayName));
    }

    /// <summary>
    /// [POST] /api/auth/login - Đăng nhập vào hệ thống
    /// </summary>
    /// <param name="request">Bao gồm Email và Password</param>
    /// <returns>AuthClientResponse chứa Access Token và thông tin user (không có Refresh Token)</returns>
    /// <remarks>
    /// Request:
    /// - Body: LoginRequest { Email, Password }
    /// 
    /// Response Success (200):
    /// Trả về accessToken (15m). Refresh Token (7 ngày) được lưu vào HttpOnly Cookie.
    /// 
    /// Response Error:
    /// - 401: Sai email, sai mật khẩu, hoặc tài khoản đã bị khóa (IsActive = false).
    /// </remarks>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);

        // Lưu RefreshToken vào HttpOnly Cookie (ẩn khỏi JavaScript)
        SetRefreshTokenCookie(result.RefreshToken);

        // Chỉ trả về AccessToken + User info, KHÔNG trả RefreshToken trong body
        return Ok(new AuthClientResponse(
            result.AccessToken,
            result.UserId,
            result.Username,
            result.DisplayName));
    }

    /// <summary>
    /// [POST] /api/auth/refresh - Cấp lại Access Token mới bằng Refresh Token
    /// </summary>
    /// <returns>AuthClientResponse với Access Token mới nhất (Rotation)</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Đọc Refresh Token từ HttpOnly Cookie "refreshToken" (không nhận từ body nữa).
    /// 2. Nếu không có Cookie → trả về 401 Unauthorized.
    /// 3. Gọi AuthService để xác minh và đổi token mới (Rotation).
    /// 4. Ghi đè Cookie cũ = Cookie mới (refresh token rotation).
    /// 5. Trả về Access Token mới cho Client.
    /// 
    /// Response Error:
    /// - 401: Cookie không tồn tại, hoặc Refresh Token không hợp lệ/đã thu hồi/hết hạn.
    /// 
    /// Side effects:
    /// - Token cũ sẽ bị đánh dấu IsRevoked = true để tránh dùng lại (chống Replay Attack).
    /// </remarks>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        // Đọc RefreshToken từ Cookie thay vì từ body JSON
        var refreshTokenFromCookie = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshTokenFromCookie))
            return Unauthorized("Refresh token cookie không tồn tại.");

        var result = await _authService.RefreshAsync(refreshTokenFromCookie);

        // Ghi đè token mới vào Cookie (Token Rotation)
        SetRefreshTokenCookie(result.RefreshToken);

        return Ok(new AuthClientResponse(
            result.AccessToken,
            result.UserId,
            result.Username,
            result.DisplayName));
    }

    /// <summary>
    /// [POST] /api/auth/logout - Khóa phiên đăng nhập hiện tại
    /// </summary>
    /// <returns>Trả về kết quả 200 OK khi đăng xuất xong</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Đọc Refresh Token từ HttpOnly Cookie "refreshToken".
    /// 2. Nếu không có Cookie → vẫn xóa Cookie và trả về 200 (idempotent).
    /// 3. Gọi AuthService để thu hồi token trong DB.
    /// 4. Xóa Cookie khỏi trình duyệt.
    /// 
    /// Side effects:
    /// - Refresh Token bị đánh dấu IsRevoked = true trong DB.
    /// - Cookie "refreshToken" bị xóa khỏi trình duyệt Client.
    /// </remarks>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshTokenFromCookie = Request.Cookies["refreshToken"];

        // Nếu có token trong Cookie thì thu hồi trong DB
        if (!string.IsNullOrEmpty(refreshTokenFromCookie))
            await _authService.LogoutAsync(refreshTokenFromCookie);

        // Xóa Cookie khỏi trình duyệt dù token có tồn tại hay không (idempotent)
        Response.Cookies.Delete("refreshToken");

        return Ok();
    }
}
