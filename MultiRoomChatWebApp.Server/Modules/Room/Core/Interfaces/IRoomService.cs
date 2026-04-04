namespace MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

public interface IRoomService
{
    /// <summary>
    /// Tìm một phòng Direct Message hiện có giữa 2 User. Nếu chưa có, tạo mới.
    /// </summary>
    /// <param name="currentUserId">ID của người đang gửi yêu cầu</param>
    /// <param name="targetUserId">ID của người nhận</param>
    /// <returns>RoomId và thông tin cơ bản của Room</returns>
    Task<Entities.Room> GetOrCreateDirectRoomAsync(Guid currentUserId, Guid targetUserId);
    
    /// <summary>
    /// Lấy danh sách các Room người dùng tham gia kèm theo bù dữ liệu từ Redis.
    /// </summary>
    Task<IEnumerable<DTOs.RoomDto>> GetMyRoomsAsync(Guid userId);
}
