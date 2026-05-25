using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.User.Services;

public class UserService : IUserService
{
    private const int MaxDisplayNameLength = 100;
    private const int MaxAvatarUrlLength = 2048;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 100;

    private readonly AppDbContext _dbContext;
    private readonly IUserCacheService _userCacheService;
    private readonly IUserRelationshipGraphService _relationshipGraphService;

    public UserService(
        AppDbContext dbContext,
        IUserCacheService userCacheService,
        IUserRelationshipGraphService relationshipGraphService)
    {
        _dbContext = dbContext;
        _userCacheService = userCacheService;
        _relationshipGraphService = relationshipGraphService;
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

        var excludedUserIds = await _relationshipGraphService.GetBlockedOrBlockingUserIdsAsync(currentUserId);

        var query = _dbContext.Users
            .AsNoTracking()
            .Where(u => u.IsActive && 
                        u.Id != currentUserId &&
                        (EF.Functions.ILike(u.Username, $"%{kw}%") ||
                         EF.Functions.ILike(u.DisplayName, $"%{kw}%")));

        if (excludedUserIds.Count > 0)
        {
            query = query.Where(u => !excludedUserIds.Contains(u.Id));
        }

        var users = await query
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
    /// Lấy profile của user đang đăng nhập theo userId trong JWT.
    /// </summary>
    /// <param name="userId">Id user hiện tại.</param>
    /// <returns>Profile public của chính user.</returns>
    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

        if (user == null)
            throw ApiException.NotFound("user_not_found", "Không tìm thấy tài khoản.");

        return MapToProfileDto(user);
    }

    /// <summary>
    /// Cập nhật profile core: displayName và avatarUrl, sau đó invalidate cache user.
    /// </summary>
    /// <param name="userId">Id user hiện tại.</param>
    /// <param name="request">Dữ liệu profile mới.</param>
    /// <returns>Profile sau khi cập nhật.</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Validate displayName/avatarUrl theo scope core.
    /// 2. Tìm user active trong DB.
    /// 3. Cập nhật SQL và UpdatedAt.
    /// 4. Invalidate Redis user cache để group/member hydrate lại dữ liệu mới.
    /// </remarks>
    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request)
    {
        var displayName = NormalizeDisplayName(request.DisplayName);
        var avatarUrl = NormalizeAvatarUrl(request.AvatarUrl);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user == null)
            throw ApiException.NotFound("user_not_found", "Không tìm thấy tài khoản.");

        user.DisplayName = displayName;
        user.AvatarUrl = avatarUrl;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await _userCacheService.InvalidateUserAsync(user.Id);

        return MapToProfileDto(user);
    }

    /// <summary>
    /// Đổi mật khẩu local và thu hồi refresh token để bắt user đăng nhập lại.
    /// </summary>
    /// <param name="userId">Id user hiện tại.</param>
    /// <param name="request">Mật khẩu hiện tại và mật khẩu mới.</param>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tìm user active và đảm bảo tài khoản có password local.
    /// 2. Verify current password bằng BCrypt.
    /// 3. Validate độ mạnh password mới theo rule đăng ký hiện tại.
    /// 4. Cập nhật PasswordHash và revoke refresh token của user.
    /// </remarks>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _dbContext.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

        if (user == null)
            throw ApiException.NotFound("user_not_found", "Không tìm thấy tài khoản.");

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
            throw ApiException.BadRequest("local_password_not_available", "Tài khoản này chưa có mật khẩu cục bộ.");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw ApiException.BadRequest("current_password_incorrect", "Mật khẩu hiện tại không đúng.");

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
            throw ApiException.BadRequest("invalid_display_name", "Tên hiển thị không được để trống.");

        if (normalized.Length > MaxDisplayNameLength)
            throw ApiException.BadRequest("invalid_display_name", $"Tên hiển thị không được vượt quá {MaxDisplayNameLength} ký tự.");

        return normalized;
    }

    private static string? NormalizeAvatarUrl(string? avatarUrl)
    {
        var normalized = avatarUrl?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;

        if (normalized.Length > MaxAvatarUrlLength)
            throw ApiException.BadRequest("invalid_avatar_url", $"Avatar URL không được vượt quá {MaxAvatarUrlLength} ký tự.");

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw ApiException.BadRequest("invalid_avatar_url", "Avatar URL phải là đường dẫn http/https hợp lệ.");
        }

        return normalized;
    }

    private static void ValidateNewPassword(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            throw ApiException.BadRequest("invalid_new_password", "Mật khẩu mới không được để trống.");

        if (newPassword.Length < MinPasswordLength || newPassword.Length > MaxPasswordLength)
            throw ApiException.BadRequest("invalid_new_password", $"Mật khẩu mới phải có từ {MinPasswordLength} đến {MaxPasswordLength} ký tự.");

        if (!newPassword.Any(char.IsUpper) ||
            !newPassword.Any(char.IsLower) ||
            !newPassword.Any(char.IsDigit) ||
            !newPassword.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            throw ApiException.BadRequest("invalid_new_password", "Mật khẩu mới phải có chữ hoa, chữ thường, chữ số và ký tự đặc biệt.");
        }
    }
}
