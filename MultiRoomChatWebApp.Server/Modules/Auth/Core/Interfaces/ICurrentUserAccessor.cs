using System.Security.Claims;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

public interface ICurrentUserAccessor
{
    ClaimsPrincipal? Principal { get; }

    bool TryGetUserId(out Guid userId);

    Guid GetUserIdOrThrow();

    string GetDisplayName(Guid? fallbackUserId = null);
}
