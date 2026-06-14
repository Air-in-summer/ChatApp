namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

// DTO cho request đăng ký tài khoản mới
public record RegisterRequest(
    string Username,
    string DisplayName,
    string Email,
    string Password);

/// <summary>
/// DTO nội bộ: thông tin identity đã được OAuth middleware xác minh.
/// Chỉ dùng trong backend, không nhận trực tiếp từ frontend.
/// </summary>
public record ExternalLoginRequest(
    string Provider,
    string ProviderUserId,
    string Email,
    string? DisplayName,
    string? AvatarUrl);

// DTO cho request đăng nhập
/// <summary>
/// Trạng thái xử lý đăng nhập external provider.
/// </summary>
public enum ExternalLoginAuthStatus
{
    Success,
    AccountConflict
}

/// <summary>
/// DTO nội bộ: kết quả đăng nhập external provider.
/// AuthResponse chỉ có giá trị khi Status = Success.
/// </summary>
public record ExternalLoginAuthResult(
    ExternalLoginAuthStatus Status,
    AuthResponse? AuthResponse);

public record LoginRequest(
    string Email,
    string Password);

/// <summary>
/// DTO nội bộ: kết quả trả về từ AuthService sang AuthController.
/// Chỉ chứa thông tin user public cần để controller tạo BFF session/response.
/// KHÔNG bao giờ được trả trực tiếp cho Client.
/// </summary>
public record AuthResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// DTO trả về cho Client (Frontend React).
/// Không chứa access token hoặc refresh token; browser auth dùng BFF session cookie.
/// </summary>
public record AuthClientResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl);
