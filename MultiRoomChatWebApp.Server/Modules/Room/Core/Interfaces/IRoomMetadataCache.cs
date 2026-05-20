using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

/// <summary>
/// Dịch vụ quản lý bộ đệm thông tin cơ bản của Room.
/// Phục vụ cho bộ điều hướng (Dispatcher) quyết định cách check quyền.
/// </summary>
public interface IRoomMetadataCache
{
    /// <summary>
    /// Lấy thông tin cơ bản của Room từ Redis. Nếu không có, query SQL và nạp lên.
    /// </summary>
    Task<(Guid? GroupId, bool IsPrivate, RoomType Type)?> GetRoomMetadataAsync(Guid roomId);

    /// <summary>
    /// Xóa Cache Metadata của Room.
    /// Gọi khi thông tin Room thay đổi (tên, avatar...) hoặc Room bị xóa.
    /// </summary>
    Task InvalidateRoomMetadataAsync(Guid roomId);

    /// <summary>
    /// Nạp chủ động Metadata của Room lên Redis (Phục vụ Cache Warm-up).
    /// </summary>
    Task SetRoomMetadataAsync(Guid roomId, Guid? groupId, bool isPrivate, RoomType type);
}
