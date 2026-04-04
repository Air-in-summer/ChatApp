using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

public interface IUserCacheService
{
    Task<UserCacheDto?> GetUserAsync(Guid userId);
}
