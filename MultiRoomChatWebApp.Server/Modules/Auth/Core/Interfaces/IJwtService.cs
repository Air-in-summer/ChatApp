using MultiRoomChatWebApp.Server.Modules.Auth.Core.Entities;
using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

public interface IJwtService
{
    /// <summary>
    /// Phát sinh JSON Web Token (JWT) ngắn hạn để xác thực request API.
    /// </summary>
    string GenerateAccessToken(AppUser user);

    /// <summary>
    /// Phát sinh token dài hạn ngẫu nhiên để hỗ trợ tái phát hành JWT.
    /// </summary>
    RefreshToken GenerateRefreshToken(Guid userId);
}
