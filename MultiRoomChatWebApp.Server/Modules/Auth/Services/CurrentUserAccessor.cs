using System.Security.Claims;
using MultiRoomChatWebApp.Server.Modules.Auth.Core;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

/// <summary>
/// Trích xuất thông tin người dùng hiện tại (ClaimsPrincipal) từ HttpContext đang hoạt động.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Khởi tạo accessor thông qua DI.
    /// </summary>
    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool TryGetUserId(out Guid userId)
    {
        return CurrentUserClaims.TryGetUserId(Principal, out userId);
    }

    public Guid GetUserIdOrThrow()
    {
        return CurrentUserClaims.GetUserIdOrThrow(Principal);
    }

    public string GetDisplayName(Guid? fallbackUserId = null)
    {
        return CurrentUserClaims.GetDisplayName(Principal, fallbackUserId);
    }
}
