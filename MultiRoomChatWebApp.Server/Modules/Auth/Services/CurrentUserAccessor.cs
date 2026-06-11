using System.Security.Claims;
using MultiRoomChatWebApp.Server.Modules.Auth.Core;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

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
