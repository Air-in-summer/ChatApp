using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;
using BCrypt.Net;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtService _jwtService;

    public AuthService(AppDbContext dbContext, IJwtService jwtService)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
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
            throw new ArgumentException("Email already in use");

        if (await _dbContext.Users.AnyAsync(u => u.Username == request.Username))
            throw new ArgumentException("Username already in use");

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
        var refreshTokenEntity = _jwtService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        // Lưu refresh token để kiểm soát phiên đăng nhập
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            accessToken, 
            refreshTokenEntity.Token, 
            user.Id, 
            user.Username, 
            user.DisplayName);
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
        
        if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials");

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenEntity = _jwtService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            accessToken, 
            refreshTokenEntity.Token, 
            user.Id, 
            user.Username, 
            user.DisplayName);
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
        var storedToken = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == refreshToken);

        if (storedToken == null || !storedToken.IsActive || !storedToken.User.IsActive)
            throw new UnauthorizedAccessException("Invalid or expired refresh token");

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
        var newRefreshTokenEntity = _jwtService.GenerateRefreshToken(user.Id);

        _dbContext.RefreshTokens.Add(newRefreshTokenEntity);
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            newAccessToken, 
            newRefreshTokenEntity.Token, 
            user.Id, 
            user.Username, 
            user.DisplayName);
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
        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == refreshToken);

        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            await _dbContext.SaveChangesAsync();
        }
    }
}
