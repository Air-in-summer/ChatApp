namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;

/// <summary>
/// Dịch vụ quản lý bộ đệm thông tin cơ bản của Group (Server).
/// Phục vụ cho hiển thị Sidebar siêu tốc.
/// </summary>
public interface IGroupMetadataCache
{
    /// <summary>
    /// Lấy thông tin cơ bản của Group từ Redis. Nếu không có, query SQL và nạp lên.
    /// </summary>
    Task<Core.DTOs.GroupDto?> GetGroupMetadataAsync(Guid groupId);

    /// <summary>
    /// Xóa Cache Metadata của Group.
    /// </summary>
    Task InvalidateGroupMetadataAsync(Guid groupId);

    /// <summary>
    /// Nạp chủ động Metadata của Group lên Redis.
    /// </summary>
    Task SetGroupMetadataAsync(Core.DTOs.GroupDto group);

    /// <summary>
    /// Lấy GroupId từ Mã mời (Tối ưu truy vấn Invite Link).
    /// </summary>
    Task<Guid?> GetGroupIdByInviteCodeAsync(string inviteCode);

    /// <summary>
    /// Lưu Mapping giữa Mã mời và GroupId.
    /// </summary>
    Task SetInviteCodeMappingAsync(string inviteCode, Guid groupId);

    /// <summary>
    /// Xóa Mapping giữa Mã mời và GroupId.
    /// </summary>
    Task InvalidateInviteCodeMappingAsync(string inviteCode);
}

