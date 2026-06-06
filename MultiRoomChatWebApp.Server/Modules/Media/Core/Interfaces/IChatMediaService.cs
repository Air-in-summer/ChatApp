using Microsoft.AspNetCore.Http;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

public interface IChatMediaService
{
    /// <summary>
    /// Upload media chat dang pending theo owner, chua gan vao room.
    /// </summary>
    Task<MediaUploadResultDto> UploadPendingAsync(
        Guid ownerUserId,
        IFormFile? file,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tao URL doc media. Pending chi owner duoc doc, attached phai qua quyen room.
    /// </summary>
    Task<MediaAccessUrlDto> CreateAccessUrlAsync(
        Guid currentUserId,
        Guid mediaId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Doc noi dung media sau khi check quyen, dung cho client render qua backend HTTPS.
    /// </summary>
    Task<MediaContentResult> OpenContentAsync(
        Guid currentUserId,
        Guid mediaId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Huy pending media khi user go file khoi composer truoc luc gui tin.
    /// </summary>
    Task CancelPendingAsync(
        Guid ownerUserId,
        Guid mediaId,
        CancellationToken cancellationToken);
}
