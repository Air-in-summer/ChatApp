namespace MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

/// <summary>
/// Dịch vụ quản lý phân quyền tham gia phòng Chat siêu tốc dựa trên Redis Set.
/// </summary>
public interface IRoomPermissionsCache
{
    /// <summary>
    /// Kiểm tra xem một người dùng có phải là thành viên của phòng hay không.
    /// Ưu tiên truy xuất từ Redis Cache (chuẩn O(1)) trước khi fall-back xuống PostgreSQL.
    /// </summary>
    /// <param name="roomId">ID của phòng</param>
    /// <param name="userId">ID của người dùng cần check</param>
    /// <returns>True nếu có quyền, False nếu không có quyền</returns>
    Task<bool> IsUserInRoomAsync(Guid roomId, Guid userId);

    /// <summary>
    /// Xóa toàn bộ Cache của một phòng, bắt hệ thống phải query lại từ SQL trong lần kế tiếp.
    /// (Sử dụng khi có người mới join/leave phòng).
    /// </summary>
    Task InvalidateRoomCacheAsync(Guid roomId);

    /// <summary>
    /// Xóa Cache Metadata của phòng (GroupId, IsPrivate).
    /// Gọi khi phòng bị xóa hoặc thay đổi thuộc tính.
    /// </summary>
    Task InvalidateRoomInfoAsync(Guid roomId);
}
