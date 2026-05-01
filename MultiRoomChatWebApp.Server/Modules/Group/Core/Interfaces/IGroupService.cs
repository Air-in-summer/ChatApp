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
}
