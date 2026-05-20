using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

public class JwtService : IJwtService
{
    private const int DefaultAccessTokenMinutes = 15;
    private const int DefaultRefreshTokenMinutes = 10080;

    private readonly IConfiguration _config;
    
    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Tạo Access Token có thời hạn 15 phút.
    /// </summary>
    /// <param name="user">Thực thể người dùng chứa Id, Email, Username</param>
    /// <returns>Chuỗi Base64 đại diện cho JWT hợp lệ</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tải các khóa (SecretKey, Issuer, Audience) từ appsettings. 
    /// 2. Khởi tạo thuật toán mã hóa đối xứng HMACSHA256.
    /// 3. Viết vào Token các Payload Claims (Sub, Email, Name, Jti).
    /// 4. Đặt thời gian hết hạn (Expires) là 15 phút từ lúc tạo.
    /// </remarks>
    public string GenerateAccessToken(AppUser user)
    {
        var secretKey = _config["Jwt:Key"] ?? throw new ArgumentNullException("Jwt:Key is missing");
        var issuer = _config["Jwt:Issuer"] ?? throw new ArgumentNullException("Jwt:Issuer is missing");
        var audience = _config["Jwt:Audience"] ?? throw new ArgumentNullException("Jwt:Audience is missing");
        
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Username),
            new Claim("displayName", user.DisplayName),
            new Claim("avatarUrl", user.AvatarUrl ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes()),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Tạo chuỗi ngẫu nhiên 32-byte an toàn dùng làm Refresh Token (7 ngày).
    /// </summary>
    /// <param name="userId">Id của chủ sở hữu token</param>
    /// <returns>Đối tượng RefreshToken chuẩn bị để persist vào DB</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// Dùng trình tạo số giả ngẫu nhiên mã hóa (RandomNumberGenerator) 
    /// trích xuất 32 byte để tạo thành Token String siêu bảo mật, chống phỏng đoán.
    /// </remarks>
    public RefreshTokenGenerationResult GenerateRefreshToken(Guid userId)
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var tokenString = Convert.ToBase64String(randomBytes);

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = HashRefreshToken(tokenString),
            ExpiresAt = DateTime.UtcNow.AddMinutes(GetRefreshTokenMinutes()),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        return new RefreshTokenGenerationResult(tokenString, entity);
    }

    /// <summary>
    /// Bam Refresh Token raw bang SHA-256 truoc khi luu hoac truy van database.
    /// </summary>
    /// <param name="refreshToken">Refresh Token raw lay tu cookie HttpOnly.</param>
    /// <returns>Chuoi hash Base64 dung de so khop voi TokenHash trong DB.</returns>
    /// <remarks>
    /// Refresh Token co entropy cao va chi raw token moi duoc gui ve browser.
    /// Database chi giu hash de giam rui ro neu du lieu bi lo.
    /// </remarks>
    public string HashRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token is required", nameof(refreshToken));

        var tokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Lay thoi han Access Token tu cau hinh, fallback ve gia tri production-safe.
    /// </summary>
    private int GetAccessTokenMinutes()
    {
        return _config.GetValue("Auth:TokenLifetime:AccessTokenMinutes", DefaultAccessTokenMinutes);
    }

    /// <summary>
    /// Lay thoi han Refresh Token tu cau hinh de dong bo voi cookie MaxAge.
    /// </summary>
    private int GetRefreshTokenMinutes()
    {
        return _config.GetValue("Auth:TokenLifetime:RefreshTokenMinutes", DefaultRefreshTokenMinutes);
    }
}
