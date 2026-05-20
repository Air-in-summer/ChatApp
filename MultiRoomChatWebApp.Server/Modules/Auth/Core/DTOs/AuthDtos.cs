using MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

// DTO cho request đăng ký tài khoản mới
public record RegisterRequest(
    string Username,
    string DisplayName,
    string Email,
    string Password);

// DTO cho request đăng nhập
public record LoginRequest(
    string Email,
    string Password);

// RefreshRequest và LogoutRequest đã bị xóa:
// RefreshToken không còn được gửi qua body JSON nữa.
// Thay vào đó, Controller đọc trực tiếp từ HttpOnly Cookie "refreshToken".

/// <summary>
/// DTO nội bộ: kết quả trả về từ AuthService sang AuthController.
/// Chứa đầy đủ thông tin kể cả RefreshToken (dùng để Controller đặt vào Cookie).
/// KHÔNG bao giờ được trả trực tiếp cho Client.
/// </summary>
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// DTO trả về cho Client (Frontend React).
/// Cố tình KHÔNG chứa RefreshToken để bảo mật chống XSS:
/// RefreshToken chỉ tồn tại trong HttpOnly Cookie mà JavaScript không thể đọc được.
/// </summary>
public record AuthClientResponse(
    string AccessToken,
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// DTO nội bộ: kết quả tạo Refresh Token.
/// PlainTextToken chỉ dùng một lần để set HttpOnly Cookie, Entity chỉ lưu TokenHash xuống DB.
/// </summary>
public record RefreshTokenGenerationResult(
    string PlainTextToken,
    RefreshToken Entity);
