namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;

/// <summary>
/// Dịch vụ quản lý phân quyền và dữ liệu Group siêu tốc dựa trên Redis.
/// </summary>
public interface IGroupPermissionsCache
{
    /// <summary>
    /// Kiểm tra xem một người dùng có phải là thành viên của Group hay không.
    /// Ưu tiên truy xuất từ Redis Set (O(1)) trước khi fall-back xuống PostgreSQL.
    /// </summary>
    /// <param name="groupId">ID của Group (Server)</param>
    /// <param name="userId">ID của người dùng cần check</param>
    /// <returns>True nếu là thành viên, False nếu không phải</returns>
    Task<bool> IsUserInGroupAsync(Guid groupId, Guid userId);

    /// <summary>
    /// Xóa Cache danh sách thành viên của một Group.
    /// Gọi khi có thành viên Join/Leave/Kick.
    /// </summary>
    Task InvalidateGroupMembersAsync(Guid groupId);
    
    /*
    /// <summary>
    /// Xóa
    /// 
    /// </summary>
    //Task AddUserToGroupAsync(Guid groupId, Guid userId);
    */

    /// <summary>
    /// Thêm 1 User vào Cache danh sách thành viên (Incremental Update).
    /// </summary>
    Task AddUserToGroupAsync(Guid groupId, Guid userId);

    /// <summary>
    /// Lấy vai trò của thành viên trong Group từ Cache.
    /// </summary>
    Task<MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole?> GetMemberRoleAsync(Guid groupId, Guid userId);

    /// <summary>
    /// Cập nhật (Ghi đè) vai trò của một thành viên trong Cache (Proactive Update).
    /// </summary>
    Task UpdateMemberRoleCacheAsync(Guid groupId, Guid userId, MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole role);

    /// <summary>
    /// Lấy toàn bộ danh sách thành viên và vai trò trong Group từ Cache.
    /// </summary>
    Task<IDictionary<Guid, MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole>> GetGroupMemberRolesAsync(Guid groupId);

    Task RemoveUserFromGroupAsync(Guid groupId, Guid userId);
}