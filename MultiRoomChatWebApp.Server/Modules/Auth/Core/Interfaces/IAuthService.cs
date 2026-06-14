using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<ExternalLoginAuthResult> LoginWithExternalProviderAsync(ExternalLoginRequest request);
}
