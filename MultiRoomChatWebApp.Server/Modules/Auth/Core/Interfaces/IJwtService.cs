using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

public interface IJwtService
{
    /// <summary>
    /// Phat sinh JSON Web Token (JWT) ngan han de xac thuc request API.
    /// </summary>
    string GenerateAccessToken(AppUser user);

    /// <summary>
    /// Phat sinh refresh token raw cho Cookie va entity chi chua TokenHash de luu DB.
    /// </summary>
    RefreshTokenGenerationResult GenerateRefreshToken(Guid userId);

    /// <summary>
    /// Bam Refresh Token raw tu Cookie de so khop voi TokenHash trong database.
    /// </summary>
    string HashRefreshToken(string refreshToken);
}
