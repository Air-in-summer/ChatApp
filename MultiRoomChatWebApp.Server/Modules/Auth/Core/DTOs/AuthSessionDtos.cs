using System.Security.Claims;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

/// <summary>
/// Metadata nhe cua request tao session, dung cho audit va quan ly phien.
/// </summary>
public sealed record AuthSessionMetadata(
    string? IpAddress,
    string? UserAgent);

/// <summary>
/// Ket qua tao session moi. Raw token chi duoc tra ve mot lan de set cookie.
/// </summary>
public sealed record AuthSessionCreationResult(
    Guid SessionId,
    string RawSessionToken,
    DateTime ExpiresAtUtc);

/// <summary>
/// Ket qua validate session dung de authentication handler tao principal/ticket.
/// </summary>
public sealed record AuthSessionValidationResult(
    Guid SessionId,
    Guid UserId,
    DateTime ExpiresAtUtc,
    IReadOnlyList<Claim> Claims);

/// <summary>
/// Ket qua gia han mot session dang hoat dong.
/// </summary>
public sealed record AuthSessionRenewalResult(
    Guid SessionId,
    DateTime ExpiresAtUtc);

/// <summary>
/// Thong tin phien cong khai cho frontend, khong chua access token hay session token.
/// </summary>
public sealed record AuthSessionResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    DateTime ExpiresAtUtc);

/// <summary>
/// Ket qua cong khai cua thao tac gia han session.
/// </summary>
public sealed record AuthSessionRenewalResponse(
    DateTime ExpiresAtUtc);

/// <summary>
/// Request token chong CSRF de frontend gui lai qua header tren unsafe request.
/// </summary>
public sealed record CsrfTokenResponse(
    string Token,
    string HeaderName);
