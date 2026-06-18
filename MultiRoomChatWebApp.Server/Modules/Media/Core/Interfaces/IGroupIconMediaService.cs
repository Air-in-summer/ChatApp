using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

public interface IGroupIconMediaService
{
    /// <summary>
    /// Upload icon moi cho Server va cap nhat metadata group.
    /// </summary>
    Task<GroupDto> UploadGroupIconAsync(
        Guid userId,
        Guid groupId,
        IFormFile? file,
        CancellationToken cancellationToken);
}
