using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core;

public static class HubUserClaims
{
    /// <summary>
    /// Doc user id tu ClaimsPrincipal cua Hub ma khong phu thuoc HttpContext.
    /// </summary>
    public static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        return CurrentUserClaims.TryGetUserId(principal, out userId);
    }

    /// <summary>
    /// Doc user id tu ClaimsPrincipal cua Hub va nem HubException neu phien khong hop le.
    /// </summary>
    public static Guid GetUserIdOrThrow(ClaimsPrincipal? principal)
    {
        return TryGetUserId(principal, out var userId)
            ? userId
            : throw new HubException("Phien dang nhap khong hop le.");
    }
}
