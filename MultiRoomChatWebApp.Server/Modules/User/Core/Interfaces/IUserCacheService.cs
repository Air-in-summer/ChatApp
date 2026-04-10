using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

public interface IUserCacheService
{
    Task<UserCacheDto?> GetUserAsync(Guid userId);

    /// <summary>
    /// Xóa bộ nhớ đệm của User khi có sự thay đổi dữ liệu (vd: Đổi tên). 
    /// Ngăn chặn rủi ro dữ liệu rác (Stale Data).
    /// </summary>
    Task InvalidateUserAsync(Guid userId);
}
