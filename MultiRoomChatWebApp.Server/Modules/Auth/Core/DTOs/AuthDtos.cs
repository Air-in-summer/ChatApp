namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

public record RegisterRequest(
    string Username,
    string DisplayName,
    string Email,
    string Password);

public record LoginRequest(
    string Email,
    string Password);

public record RefreshRequest(
    string RefreshToken);
    
public record LogoutRequest(
    string RefreshToken);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    Guid UserId,
    string Username,
    string DisplayName);
