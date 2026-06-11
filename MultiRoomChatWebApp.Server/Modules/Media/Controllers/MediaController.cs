using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Controllers;

[ApiController]
[Route("api/v1/media")]
[Authorize]
public sealed class MediaController : ControllerBase
{
    private const long MaxChatMediaRequestBytes = 110L * 1024 * 1024;

    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAvatarMediaService _avatarMediaService;
    private readonly IChatMediaService _chatMediaService;

    public MediaController(
        ICurrentUserAccessor currentUser,
        IAvatarMediaService avatarMediaService,
        IChatMediaService chatMediaService)
    {
        _currentUser = currentUser;
        _avatarMediaService = avatarMediaService;
        _chatMediaService = chatMediaService;
    }

    /// <summary>
    /// [POST] /api/v1/media/avatar - Upload avatar noi bo cho user dang dang nhap.
    /// </summary>
    /// <param name="file">File anh .jpg/.jpeg/.png/.webp gui bang multipart/form-data.</param>
    /// <returns>Profile user sau khi cap nhat avatar.</returns>
    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        var profile = await _avatarMediaService.UploadAvatarAsync(currentUserId, file, cancellationToken);
        return Ok(profile);
    }

    /// <summary>
    /// [DELETE] /api/v1/media/avatar - Reset avatar ve fallback cho user dang dang nhap.
    /// </summary>
    /// <returns>Profile user sau khi xoa avatar.</returns>
    [HttpDelete("avatar")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteAvatar(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        var profile = await _avatarMediaService.DeleteAvatarAsync(currentUserId, cancellationToken);
        return Ok(profile);
    }

    /// <summary>
    /// [POST] /api/v1/media/chat/pending - Upload chat media dang pending cho composer.
    /// </summary>
    /// <param name="file">File image/audio/video gui bang multipart/form-data.</param>
    /// <returns>Metadata pending media va signed URL preview ngan han.</returns>
    [HttpPost("chat/pending")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxChatMediaRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxChatMediaRequestBytes)]
    [ProducesResponseType(typeof(MediaUploadResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadPendingChatMedia(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        var result = await _chatMediaService.UploadPendingAsync(currentUserId, file, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// [POST] /api/v1/media/{mediaId}/access-url - Cap URL doc media sau khi check quyen.
    /// </summary>
    /// <param name="mediaId">Media id can lay URL.</param>
    /// <returns>Public URL hoac signed URL ngan han.</returns>
    [HttpPost("{mediaId:guid}/access-url")]
    [ProducesResponseType(typeof(MediaAccessUrlDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateMediaAccessUrl(
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        var result = await _chatMediaService.CreateAccessUrlAsync(currentUserId, mediaId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// [GET] /api/v1/media/{mediaId}/content - Stream media qua backend HTTPS sau khi check quyen.
    /// </summary>
    /// <param name="mediaId">Media id can render.</param>
    [HttpGet("{mediaId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMediaContent(
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        var result = await _chatMediaService.OpenContentAsync(currentUserId, mediaId, cancellationToken);

        if (result.SizeBytes.HasValue)
        {
            Response.ContentLength = result.SizeBytes.Value;
        }

        Response.Headers.CacheControl = "private, max-age=300";
        return File(result.Content, result.ContentType);
    }

    /// <summary>
    /// [DELETE] /api/v1/media/chat/pending/{mediaId} - Huy pending media trong composer.
    /// </summary>
    /// <param name="mediaId">Pending media id can huy.</param>
    [HttpDelete("chat/pending/{mediaId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelPendingChatMedia(
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserIdOrThrow();
        await _chatMediaService.CancelPendingAsync(currentUserId, mediaId, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserIdOrThrow()
    {
        return _currentUser.GetUserIdOrThrow();
    }
}
