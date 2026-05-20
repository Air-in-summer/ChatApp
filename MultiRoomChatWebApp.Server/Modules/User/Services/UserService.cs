using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.User.Services;

public class UserService : IUserService
{
    private const int MaxDisplayNameLength = 100;
    private const int MaxAvatarUrlLength = 2048;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 100;

    private readonly AppDbContext _dbContext;
    private readonly IUserCacheService _userCacheService;

    public UserService(AppDbContext dbContext, IUserCacheService userCacheService)
    {
        _dbContext = dbContext;
        _userCacheService = userCacheService;
    }

    /// <summary>
    /// Tìm kiếm người dùng theo Username hoặc DisplayName.
    /// Tăng tốc truy vấn tìm kiếm ngẫu nhiên bằng Trigram Index (GIN).
    /// </summary>
    /// <param name="keyword">Chuỗi tìm kiếm bất kỳ (Contains)</param>
    /// <param name="currentUserId">Loại bản thân khỏi kết quả</param>
    /// <returns>Tối đa 10 user khớp nhất</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Cắt khoảng trắng chuẩn bị chuỗi kw.
    /// 2. Áp dụng EF.Functions.ILike thay thế StartsWith. Hàm này tự bỏ qua phân biệt hoa thường.
    /// 3. Sử dụng ký tự đại diện % ở 2 đầu (Contains Search).
    /// 4. LUÔN LUÔN Bắt buộc có .OrderBy để chống lại lỗi Unpredictable Results của PostgreSQL.
    /// </remarks>
    public async Task<IEnumerable<UserSearchDto>> SearchByKeywordAsync(string keyword, Guid currentUserId)
    {
        var kw = keyword.Trim()
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        if (string.IsNullOrEmpty(kw))
            return Enumerable.Empty<UserSearchDto>();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.IsActive && 
                        u.Id != currentUserId &&
                        (EF.Functions.ILike(u.Username, $"%{kw}%") ||
                         EF.Functions.ILike(u.DisplayName, $"%{kw}%")))
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserSearchDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl
            })
            .Take(10)
            .ToListAsync();

        return users;
    }

    /// <summary>
    /// Lay profile cua user dang dang nhap theo userId trong JWT.
    /// </summary>
    /// <param name="userId">Id user hien tai.</param>
    /// <returns>Profile public cua chinh user.</returns>
    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

        if (user == null)
            throw new KeyNotFoundException("User not found");

        return MapToProfileDto(user);
    }

    /// <summary>
    /// Cap nhat profile core: displayName va avatarUrl, sau do invalidate cache user.
    /// </summary>
    /// <param name="userId">Id user hien tai.</param>
    /// <param name="request">Du lieu profile moi.</param>
    /// <returns>Profile sau khi cap nhat.</returns>
    /// <remarks>
    /// Luong xu ly:
    /// 1. Validate displayName/avatarUrl theo scope core.
    /// 2. Tim user active trong DB.
    /// 3. Cap nhat SQL va UpdatedAt.
    /// 4. Invalidate Redis user cache de group/member hydrate lai du lieu moi.
    /// </remarks>
    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request)
    {
        var displayName = NormalizeDisplayName(request.DisplayName);
        var avatarUrl = NormalizeAvatarUrl(request.AvatarUrl);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user == null)
            throw new KeyNotFoundException("User not found");

        user.DisplayName = displayName;
        user.AvatarUrl = avatarUrl;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await _userCacheService.InvalidateUserAsync(user.Id);

        return MapToProfileDto(user);
    }

    /// <summary>
    /// Doi mat khau local va thu hoi refresh token de bat user dang nhap lai.
    /// </summary>
    /// <param name="userId">Id user hien tai.</param>
    /// <param name="request">Mat khau hien tai va mat khau moi.</param>
    /// <remarks>
    /// Luong xu ly:
    /// 1. Tim user active va dam bao tai khoan co password local.
    /// 2. Verify current password bang BCrypt.
    /// 3. Validate do manh password moi theo rule dang ky hien tai.
    /// 4. Cap nhat PasswordHash va revoke refresh token cua user.
    /// </remarks>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _dbContext.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

        if (user == null)
            throw new KeyNotFoundException("User not found");

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
            throw new InvalidOperationException("This account does not have a local password");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect");

        ValidateNewPassword(request.NewPassword);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        foreach (var token in user.RefreshTokens.Where(t => t.IsActive))
        {
            token.IsRevoked = true;
        }

        await _dbContext.SaveChangesAsync();
    }

    private static UserProfileDto MapToProfileDto(Core.Entities.User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl
        };
    }

    private static string NormalizeDisplayName(string displayName)
    {
        var normalized = displayName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("DisplayName is required");

        if (normalized.Length > MaxDisplayNameLength)
            throw new ArgumentException($"DisplayName must be at most {MaxDisplayNameLength} characters");

        return normalized;
    }

    private static string? NormalizeAvatarUrl(string? avatarUrl)
    {
        var normalized = avatarUrl?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;

        if (normalized.Length > MaxAvatarUrlLength)
            throw new ArgumentException($"AvatarUrl must be at most {MaxAvatarUrlLength} characters");

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("AvatarUrl must be an absolute http/https URL");
        }

        return normalized;
    }

    private static void ValidateNewPassword(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("New password is required");

        if (newPassword.Length < MinPasswordLength || newPassword.Length > MaxPasswordLength)
            throw new ArgumentException($"New password must be {MinPasswordLength}-{MaxPasswordLength} characters");

        if (!newPassword.Any(char.IsUpper) ||
            !newPassword.Any(char.IsLower) ||
            !newPassword.Any(char.IsDigit) ||
            !newPassword.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            throw new ArgumentException("New password must include uppercase, lowercase, number and special character");
        }
    }
}
