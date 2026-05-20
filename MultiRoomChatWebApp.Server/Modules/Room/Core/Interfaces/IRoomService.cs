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

    /// <summary>
    /// Tạo một Room (Channel) mới bên trong một Group/Server.
    /// </summary>
    Task<Entities.Room> CreateGroupRoomAsync(string name, Core.Enums.RoomType type, bool isPrivate, Guid createdBy, Guid groupId);

    /// <summary>
    /// Thêm một User vào bảng RoomMembers của tất cả các phòng Public trong một Group.
    /// Được gọi khi User vừa tham gia Group qua Invite Code.
    /// </summary>
    /// <param name="groupId">ID của Group vừa tham gia</param>
    /// <param name="userId">ID của User mới tham gia</param>
    /// <remarks>
    /// Lưu ý: Không cập nhật Room Cache (chủ đích: phòng Public không dùng Room Cache).
    /// </remarks>
    Task AddUserToPublicRoomsAsync(Guid groupId, Guid userId);

    /// <summary>
    /// Thêm danh sách thành viên vào một phòng Private.
    /// Caller (Controller) chịu trách nhiệm check quyền và validate userIds thuộc Group trước khi gọi.
    /// </summary>
    /// <param name="roomId">ID của phòng Private cần thêm thành viên</param>
    /// <param name="userIds">Danh sách UserIds đã được validate (thuộc Group, chưa thuộc phòng)</param>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Validate phòng: Phải là Private và có GroupId.
    /// 2. Lọc trùng SQL: Loại bỏ userId đã có trong RoomMembers.
    /// 3. Bulk Insert vào RoomMembers (Role = Member).
    /// 4. Cập nhật Cache: SADD batch vào Redis.
    /// 5. Publish Domain Event để thông báo SignalR.
    /// </remarks>
    Task AddMembersToPrivateRoomAsync(Guid roomId, IEnumerable<Guid> userIds);

    /// <summary>
    /// Lấy danh sách ID thành viên hiện tại của phòng (từ Cache).
    /// Hỗ trợ cho Frontend lọc danh sách hiển thị.
    /// </summary>
    Task<IEnumerable<Guid>> GetRoomMemberIdsAsync(Guid roomId);
}
