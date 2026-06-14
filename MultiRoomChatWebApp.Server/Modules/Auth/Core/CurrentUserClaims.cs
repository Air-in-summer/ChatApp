using System.Security.Claims;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core;

public static class CurrentUserClaims
{
    public const string SubjectClaim = "sub";
    public const string NameClaim = "name";

    public static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        userId = Guid.Empty;

        var userIdString = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue(SubjectClaim);

        return Guid.TryParse(userIdString, out userId);
    }

    public static Guid GetUserIdOrThrow(ClaimsPrincipal? principal)
    {
        return TryGetUserId(principal, out var userId)
            ? userId
            : throw ApiException.Unauthorized(
                "user_context_missing",
                "Phien dang nhap khong hop le. Vui long dang nhap lai.");
    }

    public static string GetDisplayName(ClaimsPrincipal? principal, Guid? fallbackUserId = null)
    {
        return principal?.FindFirstValue("displayName")
            ?? principal?.FindFirstValue(ClaimTypes.Name)
            ?? principal?.FindFirstValue(NameClaim)
            ?? fallbackUserId?.ToString()
            ?? "Unknown";
    }
}
