using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

public interface IAuthSessionService
{
    /// <summary>
    /// Tao session server-side moi cho user dang hoat dong.
    /// </summary>
    Task<AuthSessionCreationResult> CreateSessionAsync(
        Guid userId,
        AuthSessionMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate raw session token va tra claims neu session con hieu luc.
    /// </summary>
    Task<AuthSessionValidationResult?> ValidateSessionAsync(
        string? rawSessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gia han session dang hoat dong ma khong rotate raw token.
    /// </summary>
    Task<AuthSessionRenewalResult?> RenewSessionAsync(
        string? rawSessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke mot session theo raw token. Thao tac idempotent.
    /// </summary>
    Task<bool> RevokeSessionAsync(
        string? rawSessionToken,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke toan bo session dang hoat dong cua mot user.
    /// </summary>
    Task<int> RevokeAllUserSessionsAsync(
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
