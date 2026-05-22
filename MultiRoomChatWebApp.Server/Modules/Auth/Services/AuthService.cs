using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;
using BCrypt.Net;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext dbContext, IJwtService jwtService, ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _logger = logger;
    }

    /// <summary>
    /// Tạo username hợp lệ từ email/displayName Google và đảm bảo không trùng.
    /// </summary>
    /// <param name="email">Email đã được normalize.</param>
    /// <param name="displayName">Tên hiển thị Google nếu có.</param>
    /// <returns>Username hợp lệ theo rule đăng ký hiện tại.</returns>
    private async Task<string> GenerateUniqueGoogleUsernameAsync(string email, string? displayName)
    {
        var source = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(source))
            source = displayName ?? string.Empty;

        var allowedChars = source
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_')
            .ToArray();

        var seed = new string(allowedChars).Trim('.', '_');
        if (seed.Length < 3)
            seed = "googleuser";

        if (seed.Length > 30)
            seed = seed[..30];

        if (!await _dbContext.Users.AnyAsync(user => user.Username == seed))
            return seed;

        for (var suffix = 1; suffix <= 99; suffix++)
        {
            var suffixText = $".{suffix}";
            var prefixLength = Math.Min(seed.Length, 30 - suffixText.Length);
            var candidate = $"{seed[..prefixLength]}{suffixText}";
            if (!await _dbContext.Users.AnyAsync(user => user.Username == candidate))
                return candidate;
        }

        return $"google.{Guid.NewGuid():N}"[..30];
    }

    /// <summary>
    /// Chuẩn hóa displayName nhận từ Google theo rule profile core.
    /// </summary>
    /// <param name="displayName">Tên hiển thị Google nếu có.</param>
    /// <param name="fallback">Giá trị fallback khi displayName không hợp lệ.</param>
    /// <returns>DisplayName không rỗng, tối đa 100 ký tự.</returns>
    private static string NormalizeGoogleDisplayName(string? displayName, string fallback)
    {
        var normalized = string.Join(' ', (displayName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Length < 2)
            normalized = fallback;

        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    /// <summary>
    /// Chỉ chấp nhận avatar URL tuyệt đối http/https từ Google.
    /// </summary>
    /// <param name="avatarUrl">URL avatar Google picture claim.</param>
    /// <returns>Avatar URL hợp lệ hoặc null.</returns>
    private static string? NormalizeGoogleAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl) || avatarUrl.Length > 2048)
            return null;

        return Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https"
            ? avatarUrl
            : null;
    }

    /// <summary>
    /// Đăng ký người dùng mới, hash mật khẩu và cung cấp Token khởi tạo.
    /// </summary>
    /// <param name="request">Thông tin đăng ký (Username, Email, DisplayName, Password)</param>
    /// <returns>AuthResponse với Access Token và Refresh Token</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Kiểm tra Email và Username xem đã tồn tại trong hệ thống chưa? Nếu có => Throw BadRequest error.
    /// 2. Tạo đối tượng User, đặt DisplayName, và mã hóa Password dùng thuật toán BCrypt.
    /// 3. Lưu User xuống Database.
    /// 4. Phát sinh Access Token và Refresh Token qua JwtService.
    /// 5. Lưu Refresh Token liên kết với User xuống Database. 
    /// </remarks>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail))
            throw ApiException.Conflict("email_already_exists", "Email đã được sử dụng.");

        if (await _dbContext.Users.AnyAsync(u => u.Username == request.Username))
            throw ApiException.Conflict("username_already_exists", "Username đã được sử dụng.");

        var user = new AppUser
        {
            Username = request.Username,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Username : request.DisplayName,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Tạo 2 token (ngắn hạn và dài hạn) cung cấp cho client
        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenResult = _jwtService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshTokenResult.Entity);
        // Lưu refresh token để kiểm soát phiên đăng nhập
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            accessToken, 
            refreshTokenResult.PlainTextToken, 
            user.Id, 
            user.Username, 
            user.DisplayName,
            user.AvatarUrl);
    }

    /// <summary>
    /// Rà soát mật khẩu và đăng nhập hệ thống. Tự động từ chối tài khoản IsActive=false.
    /// </summary>
    /// <param name="request">Thông tin Email và Password</param>
    /// <returns>AuthResponse chứa phiên làm việc mới</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Truy vấn User theo thư Email.
    /// 2. Kiểm tra nếu User null, bị cấm, hoặc mật khẩu xác thực Verify() bị sai => Throw Unauthorized.
    /// 3. Sinh Token pair qua JwtService.
    /// 4. Lưu lại Refresh Token để bắt đầu phiên mới cho user này.
    /// </remarks>
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        
        if (user == null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw ApiException.Unauthorized("invalid_credentials", "Email hoặc mật khẩu không đúng.");

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenResult = _jwtService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshTokenResult.Entity);
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            accessToken, 
            refreshTokenResult.PlainTextToken, 
            user.Id, 
            user.Username, 
            user.DisplayName,
            user.AvatarUrl);
    }

    /// <summary>
    /// Đăng nhập bằng external provider hoặc tạo user mới nếu email chưa tồn tại.
    /// </summary>
    /// <param name="request">Thông tin provider identity đã được OAuth middleware xác minh.</param>
    /// <returns>Kết quả đăng nhập external provider với status rõ nghĩa.</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tìm ExternalLogin theo Provider + ProviderUserId.
    /// 2. Nếu chưa có mapping và email đã tồn tại thì trả null để 2.6 xử lý account conflict.
    /// 3. Nếu chưa có mapping và email mới thì tạo user Google-only + ExternalLogin trong transaction.
    /// 4. Nếu mapping tồn tại nhưng user nội bộ inactive thì từ chối đăng nhập.
    /// 5. Phát JWT nội bộ và refresh token như login password.
    /// </remarks>
    public async Task<ExternalLoginAuthResult> LoginWithExternalProviderAsync(ExternalLoginRequest request)
    {
        var externalLogin = await _dbContext.ExternalLogins
            .Include(login => login.User)
            .FirstOrDefaultAsync(login =>
                login.Provider == request.Provider &&
                login.ProviderUserId == request.ProviderUserId);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (externalLogin == null)
        {
            if (await _dbContext.Users.AnyAsync(user => user.Email == normalizedEmail))
            {
                return new ExternalLoginAuthResult(
                    ExternalLoginAuthStatus.AccountConflict,
                    null);
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var username = await GenerateUniqueGoogleUsernameAsync(normalizedEmail, request.DisplayName);
            var newUser = new AppUser
            {
                Username = username,
                DisplayName = NormalizeGoogleDisplayName(request.DisplayName, username),
                Email = normalizedEmail,
                PasswordHash = null,
                AvatarUrl = NormalizeGoogleAvatarUrl(request.AvatarUrl),
                IsActive = true
            };

            _dbContext.Users.Add(newUser);
            _dbContext.ExternalLogins.Add(new ExternalLogin
            {
                User = newUser,
                Provider = request.Provider,
                ProviderUserId = request.ProviderUserId,
                ProviderEmail = normalizedEmail
            });

            var newUserAccessToken = _jwtService.GenerateAccessToken(newUser);
            var newUserRefreshTokenResult = _jwtService.GenerateRefreshToken(newUser.Id);

            _dbContext.RefreshTokens.Add(newUserRefreshTokenResult.Entity);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Đã tạo user mới {UserId} từ external provider {Provider}.",
                newUser.Id,
                request.Provider);

            return new ExternalLoginAuthResult(
                ExternalLoginAuthStatus.Success,
                new AuthResponse(
                    newUserAccessToken,
                    newUserRefreshTokenResult.PlainTextToken,
                    newUser.Id,
                    newUser.Username,
                    newUser.DisplayName,
                    newUser.AvatarUrl));
        }

        var user = externalLogin.User;
        if (!user.IsActive)
            throw ApiException.Unauthorized("user_inactive", "Tài khoản đang bị khóa.");

        externalLogin.ProviderEmail = normalizedEmail;
        externalLogin.UpdatedAt = DateTime.UtcNow;

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenResult = _jwtService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshTokenResult.Entity);
        await _dbContext.SaveChangesAsync();

        return new ExternalLoginAuthResult(
            ExternalLoginAuthStatus.Success,
            new AuthResponse(
                accessToken,
                refreshTokenResult.PlainTextToken,
                user.Id,
                user.Username,
                user.DisplayName,
                user.AvatarUrl));
    }

    /// <summary>
    /// Xoay vòng (Rotation) Refresh Token để sinh ra Access Token mới.
    /// </summary>
    /// <param name="refreshToken">Chuỗi token cũ đang chưa bị thu hồi</param>
    /// <returns>AuthResponse chứa Refresh Token và Access Token hoàn toàn mới</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tìm Refresh Token trong hệ thống.
    /// 2. Xác thực tính hiệu lực (token phải chưa hết hạn, và chưa bị mark IsRevoked).
    /// 3. Thực hiện [Lazy Cleanup]: tìm và làm sạch các token rác khác của ông user đó.
    /// 4. Vô hiệu hóa chính cái Refresh Token vừa gửi lên (Rotation security pattern).
    /// 5. Đẻ ra cái cặp Token mới rồi ném về client.
    /// </remarks>
    public async Task<AuthResponse> RefreshAsync(string refreshToken)
    {
        var refreshTokenHash = _jwtService.HashRefreshToken(refreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == refreshTokenHash);

        if (storedToken == null || !storedToken.IsActive || !storedToken.User.IsActive)
            throw ApiException.Unauthorized("refresh_token_invalid", "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");

        // [Luồng Lazy Cleanup]
        // Xác định và xóa sạch các token đã rác của người dùng để nhẹ dọn CSDL
        // Lý do: Đỡ phụ thuộc 100% vào Background Service
        var oldTokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == storedToken.UserId && (t.IsRevoked || t.ExpiresAt <= DateTime.UtcNow))
            .ToListAsync();
            
        if (oldTokens.Any())
        {
            _dbContext.RefreshTokens.RemoveRange(oldTokens);
        }

        // Tự động thu hồi token hiện tại ngay lập tức sau khi dùng (cơ chế Token Rotation chặn replay attack)
        storedToken.IsRevoked = true;

        var user = storedToken.User;
        var newAccessToken = _jwtService.GenerateAccessToken(user);
        var newRefreshTokenResult = _jwtService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(newRefreshTokenResult.Entity);
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            newAccessToken, 
            newRefreshTokenResult.PlainTextToken, 
            user.Id, 
            user.Username, 
            user.DisplayName,
            user.AvatarUrl);
    }

    /// <summary>
    /// Hủy phiên đăng nhập thông qua Refresh Token cung cấp.
    /// </summary>
    /// <param name="refreshToken">RefreshToken đang hoạt động</param>
    /// <remarks>
    /// Luồng xử lý:
    /// Chỉ đơn giản tìm token trong DB và update cờ IsRevoked = true.
    /// Access token sẽ tiếp tục sống 15 phút rởm nhưng refresh sẽ hoàn toàn bị tịt.
    /// </remarks>
    public async Task LogoutAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var refreshTokenHash = _jwtService.HashRefreshToken(refreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == refreshTokenHash);

        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            await _dbContext.SaveChangesAsync();
        }
    }
}
