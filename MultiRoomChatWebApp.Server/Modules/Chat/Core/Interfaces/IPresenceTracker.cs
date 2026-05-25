namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Quản lý trạng thái kết nối realtime của người dùng trên toàn hệ thống.
/// </summary>
public interface IPresenceTracker
{
    /// <summary>
    /// Đánh dấu một user vừa mở kết nối realtime.
    /// </summary>
    /// <returns>True nếu đây là connection đầu tiên làm user chuyển sang online.</returns>
    Task<bool> UserConnected(Guid userId, string connectionId);

    /// <summary>
    /// Đánh dấu một user vừa ngắt một kết nối realtime.
    /// </summary>
    /// <returns>True nếu đây là connection cuối cùng làm user chuyển sang offline.</returns>
    Task<bool> UserDisconnected(Guid userId, string connectionId);

    /// <summary>
    /// Gia hạn connection đang hoạt động của user.
    /// </summary>
    /// <returns>True nếu heartbeat làm user chuyển từ offline sang online.</returns>
    Task<bool> TouchHeartbeatAsync(Guid userId, string connectionId);

    /// <summary>
    /// Kiểm tra user có ít nhất một connection còn hạn hay không.
    /// </summary>
    Task<bool> IsOnlineAsync(Guid userId);

    /// <summary>
    /// Lấy danh sách user đang online theo một danh sách ID truyền vào.
    /// </summary>
    Task<IEnumerable<Guid>> GetOnlineUsersAsync(IEnumerable<Guid> userIds);

    /// <summary>
    /// Dọn connection hết hạn và trả về các user vừa chuyển sang offline do TTL expiry.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> CleanupExpiredConnectionsAsync(CancellationToken cancellationToken = default);
}
