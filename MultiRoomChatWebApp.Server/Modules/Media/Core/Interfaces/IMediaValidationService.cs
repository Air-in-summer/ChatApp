using Microsoft.AspNetCore.Http;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

public interface IMediaValidationService
{
    /// <summary>
    /// Validate file avatar upload: size, extension, MIME va magic bytes.
    /// </summary>
    Task<ValidatedMediaFile> ValidateAvatarAsync(IFormFile? file, CancellationToken cancellationToken);

    /// <summary>
    /// Validate file chat media pending upload: size, extension, MIME va magic bytes.
    /// </summary>
    Task<ValidatedMediaFile> ValidateChatMediaAsync(IFormFile? file, CancellationToken cancellationToken);
}
