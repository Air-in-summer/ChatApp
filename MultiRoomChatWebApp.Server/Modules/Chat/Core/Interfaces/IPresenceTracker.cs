namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Quản lý trạng thái theo dõi kết nối của người dùng trên toàn hệ thống.
/// </summary>
public interface IPresenceTracker
{
    /// <summary>
    /// Đánh dấu một User vừa mở kết nối.
    /// </summary>
    /// <returns>True nếu đây là kết nối đầu tiên (User vừa chuyển sang Online)</returns>
    Task<bool> UserConnected(Guid userId, string connectionId);

    /// <summary>
    /// Đánh dấu một User vừa ngắt một kết nối.
    /// </summary>
    /// <returns>True nếu đây là kết nối cuối cùng (User đã hoàn toàn Offline)</returns>
    Task<bool> UserDisconnected(Guid userId, string connectionId);

    /// <summary>
    /// Lấy danh sách những user đang Online theo một danh sách ID truyền vào.
    /// (Thường dùng để render danh sách bạn bè/phòng chat).
    /// </summary>
    Task<IEnumerable<Guid>> GetOnlineUsersAsync(IEnumerable<Guid> userIds);
}
