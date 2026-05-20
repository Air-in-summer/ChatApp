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
    /// Xóa một người dùng cụ thể khỏi Cache của phòng (dùng lệnh SREM của Redis).
    /// Giúp dọn dẹp cache bảo mật ngay lập tức mà không cần nạp lại toàn bộ member list.
    /// </summary>
    Task RemoveUserFromRoomAsync(Guid roomId, Guid userId);

    /// <summary>
    /// Thêm danh sách người dùng vào Cache của phòng (dùng lệnh SADD của Redis).
    /// Chỉ thực hiện nếu Cache đang tồn tại để tránh nạp dữ liệu thiếu.
    /// </summary>
    Task AddUsersToRoomCacheAsync(Guid roomId, IEnumerable<Guid> userIds);

    /// <summary>
    /// Lấy toàn bộ danh sách thành viên của phòng từ Cache (Redis Set).
    /// Nếu Cache Miss, tự động nạp từ SQL lên Redis rồi trả về kết quả.
    /// </summary>
    Task<IEnumerable<Guid>> GetRoomMemberIdsAsync(Guid roomId);
}
