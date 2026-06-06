using Microsoft.AspNetCore.Http;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

public interface IAvatarMediaService
{
    /// <summary>
    /// Upload avatar moi cho user va tra ve profile da cap nhat.
    /// </summary>
    Task<UserProfileDto> UploadAvatarAsync(Guid userId, IFormFile? file, CancellationToken cancellationToken);

    /// <summary>
    /// Reset avatar cua user ve fallback va tra ve profile da cap nhat.
    /// </summary>
    Task<UserProfileDto> DeleteAvatarAsync(Guid userId, CancellationToken cancellationToken);
}
