using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Auth.Core;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

/// <summary>
/// Quan ly opaque BFF session duoc persist phia server.
/// </summary>
public sealed class AuthSessionService : IAuthSessionService
{
    private const int SessionTokenBytes = 32;
    private const int DefaultSessionLifetimeMinutes = 10080;
    private const int DefaultLastSeenUpdateIntervalMinutes = 5;

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AuthSessionService(
        AppDbContext dbContext,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<AuthSessionCreationResult> CreateSessionAsync(
        Guid userId,
        AuthSessionMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var userIsActive = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken);

        if (!userIsActive)
        {
            throw ApiException.Unauthorized(
                "session_user_invalid",
                "Tai khoan khong ton tai hoac da bi khoa.");
        }

        var now = DateTime.UtcNow;
        var rawSessionToken = GenerateRawSessionToken();
        var session = new AuthSession
        {
            UserId = userId,
            TokenHash = HashSessionToken(rawSessionToken),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.Add(GetSessionLifetime()),
            CreatedByIp = NormalizeMetadata(metadata.IpAddress, 64),
            LastSeenIp = NormalizeMetadata(metadata.IpAddress, 64),
            UserAgent = NormalizeMetadata(metadata.UserAgent, 512)
        };

        _dbContext.AuthSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthSessionCreationResult(
            session.Id,
            rawSessionToken,
            session.ExpiresAt);
    }

    /// <inheritdoc />
    public async Task<AuthSessionValidationResult?> ValidateSessionAsync(
        string? rawSessionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionToken))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var tokenHash = HashSessionToken(rawSessionToken);
        var session = await _dbContext.AuthSessions
            .AsNoTracking()
            .Include(item => item.User)
            .FirstOrDefaultAsync(
                item =>
                    item.TokenHash == tokenHash &&
                    item.RevokedAt == null &&
                    item.ExpiresAt > now &&
                    item.User.IsActive,
                cancellationToken);

        if (session == null)
        {
            return null;
        }

        if (session.LastSeenAt <= now.Subtract(GetLastSeenUpdateInterval()))
        {
            await _dbContext.AuthSessions
                .Where(item =>
                    item.Id == session.Id &&
                    item.RevokedAt == null &&
                    item.ExpiresAt > now)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.LastSeenAt, now),
                    cancellationToken);
        }

        return new AuthSessionValidationResult(
            session.Id,
            session.UserId,
            session.ExpiresAt,
            BuildClaims(session.User));
    }

    /// <inheritdoc />
    public async Task<AuthSessionRenewalResult?> RenewSessionAsync(
        string? rawSessionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionToken))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var tokenHash = HashSessionToken(rawSessionToken);
        var session = await _dbContext.AuthSessions
            .AsNoTracking()
            .Where(item =>
                item.TokenHash == tokenHash &&
                item.RevokedAt == null &&
                item.ExpiresAt > now &&
                item.User.IsActive)
            .Select(item => new { item.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            return null;
        }

        var newExpiresAt = now.Add(GetSessionLifetime());
        var affectedRows = await _dbContext.AuthSessions
            .Where(item =>
                item.Id == session.Id &&
                item.RevokedAt == null &&
                item.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.ExpiresAt, newExpiresAt)
                    .SetProperty(item => item.LastSeenAt, now),
                cancellationToken);

        return affectedRows == 1
            ? new AuthSessionRenewalResult(session.Id, newExpiresAt)
            : null;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeSessionAsync(
        string? rawSessionToken,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionToken))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var tokenHash = HashSessionToken(rawSessionToken);
        var normalizedReason = NormalizeMetadata(reason, 200);
        var affectedRows = await _dbContext.AuthSessions
            .Where(item => item.TokenHash == tokenHash && item.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.RevokedAt, now)
                    .SetProperty(item => item.RevokedReason, normalizedReason),
                cancellationToken);

        return affectedRows == 1;
    }

    /// <inheritdoc />
    public Task<int> RevokeAllUserSessionsAsync(
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var normalizedReason = NormalizeMetadata(reason, 200);

        return _dbContext.AuthSessions
            .Where(item =>
                item.UserId == userId &&
                item.RevokedAt == null &&
                item.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.RevokedAt, now)
                    .SetProperty(item => item.RevokedReason, normalizedReason),
                cancellationToken);
    }

    private static string GenerateRawSessionToken()
    {
        return WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(SessionTokenBytes));
    }

    private static string HashSessionToken(string rawSessionToken)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(rawSessionToken);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }

    private TimeSpan GetSessionLifetime()
    {
        var configuredMinutes =
            _configuration.GetValue<int?>("Auth:Bff:SessionLifetimeMinutes") ??
            DefaultSessionLifetimeMinutes;

        return TimeSpan.FromMinutes(Math.Max(1, configuredMinutes));
    }

    private TimeSpan GetLastSeenUpdateInterval()
    {
        var configuredMinutes =
            _configuration.GetValue<int?>("Auth:Bff:LastSeenUpdateIntervalMinutes") ??
            DefaultLastSeenUpdateIntervalMinutes;

        return TimeSpan.FromMinutes(Math.Max(1, configuredMinutes));
    }

    private static IReadOnlyList<Claim> BuildClaims(AppUser user)
    {
        var userId = user.Id.ToString();

        return
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(CurrentUserClaims.SubjectClaim, userId),
            new Claim(CurrentUserClaims.NameClaim, user.Username),
            new Claim("displayName", user.DisplayName),
            new Claim("avatarUrl", user.AvatarUrl ?? string.Empty)
        ];
    }

    private static string? NormalizeMetadata(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
