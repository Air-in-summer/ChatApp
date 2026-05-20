using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;

public interface IGroupService
{
    /// <summary>
    /// Tạo một Server mới.
    /// </summary>
    /// <param name="userId">ID của người tạo (sẽ trở thành Owner)</param>
    /// <param name="request">Thông tin Server</param>
    /// <returns>Thông tin Server vừa tạo</returns>
    Task<GroupDto> CreateGroupAsync(Guid userId, CreateGroupRequest request);

    /// <summary>
    /// Tạo một Channel (Room) mới trong Server.
    /// Yêu cầu người tạo phải là Owner hoặc Admin của Server.
    /// </summary>
    /// <param name="userId">ID người yêu cầu tạo (phải có quyền)</param>
    /// <param name="groupId">ID của Server</param>
    /// <param name="request">Thông tin Channel</param>
    /// <returns>RoomId vừa được tạo</returns>
    Task<Guid> CreateGroupChannelAsync(Guid userId, Guid groupId, CreateGroupChannelRequest request);

    /// <summary>
    /// Tham gia vào Server thông qua mã mời (Invite Code).
    /// </summary>
    /// <param name="userId">ID của user muốn tham gia</param>
    /// <param name="inviteCode">Mã mời của Server</param>
    /// <returns>Thông tin Server vừa tham gia</returns>
    Task<GroupDto> JoinGroupByInviteCodeAsync(Guid userId, string inviteCode);

    /// <summary>
    /// Lấy danh sách các Server (Group) mà người dùng đang tham gia.
    /// </summary>
    Task<IEnumerable<GroupDto>> GetMyGroupsAsync(Guid userId);

    /// <summary>
    /// Lấy danh sách các Room (Channel) trong một Group dựa trên quyền truy cập của User.
    /// </summary>
    Task<IEnumerable<MultiRoomChatWebApp.Server.Modules.Room.Core.DTOs.RoomDto>> GetGroupRoomsAsync(Guid groupId, Guid userId);

    /// <summary>
    /// Lấy danh sách thành viên trong một Group.
    /// </summary>
    Task<IEnumerable<GroupMemberDto>> GetGroupMembersAsync(Guid groupId);


    /// <summary>
    /// Cập nhật thông tin Server (Name, Description, Icon).
    /// </summary>
    Task<GroupDto> UpdateGroupAsync(Guid userId, Guid groupId, UpdateGroupRequest request);

    /// <summary>
    /// Bổ nhiệm hoặc bãi miễn vai trò Admin cho một thành viên.
    /// </summary>
    Task UpdateMemberRoleAsync(Guid ownerId, Guid groupId, Guid targetUserId, MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole newRole);

    /// <summary>
    /// Chuyển nhượng quyền sở hữu tối cao (Owner) cho thành viên khác.
    /// </summary>
    Task TransferOwnershipAsync(Guid currentOwnerId, Guid groupId, Guid newOwnerId);

    /// <summary>
    /// Rời khỏi Server. Owner không thể rời nếu chưa chuyển nhượng quyền.
    /// </summary>
    Task LeaveGroupAsync(Guid userId, Guid groupId);

    /// <summary>
    /// Trục xuất thành viên khỏi Server.
    /// </summary>
    Task KickMemberAsync(Guid adminId, Guid groupId, Guid targetUserId);

    /// <summary>
    /// Giải tán (Soft Delete) Server. Chỉ Owner mới có quyền.
    /// </summary>
    Task SoftDeleteGroupAsync(Guid ownerId, Guid groupId);
}


