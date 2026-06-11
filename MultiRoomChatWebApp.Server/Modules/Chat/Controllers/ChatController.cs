using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Controllers;

[ApiController]
[Route("api/v1/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IChatService _chatService;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly Modules.Group.Core.Interfaces.IGroupPermissionsCache _groupPermissionsCache;
    private readonly IMessageMutationService _messageMutationService;

    public ChatController(
        ICurrentUserAccessor currentUser,
        IChatService chatService, 
        IRoomPermissionsCache roomPermissionsCache,
        IRoomMetadataCache roomMetadataCache,
        Modules.Group.Core.Interfaces.IGroupPermissionsCache groupPermissionsCache,
        IMessageMutationService messageMutationService)
    {
        _currentUser = currentUser;
        _chatService = chatService;
        _roomPermissionsCache = roomPermissionsCache;
        _roomMetadataCache = roomMetadataCache;
        _groupPermissionsCache = groupPermissionsCache;
        _messageMutationService = messageMutationService;
    }

    /// <summary>
    /// Lấy lịch sử tin nhắn của một phòng với tính năng Lazy Load (Cursor-based Pagination)
    /// </summary>
    /// <param name="roomId">ID của phòng</param>
    /// <param name="cursor">Opaque cursor do lần tải lịch sử trước trả về</param>
    /// <param name="limit">Số lượng lấy</param>
    [HttpGet("rooms/{roomId:guid}/messages")]
    [ProducesResponseType(typeof(IEnumerable<Message>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMessages(Guid roomId, [FromQuery] string? beforeMessageId = null, [FromQuery] int limit = 50)
    {
        if (limit <= 0 || limit > 100)
        {
            return BadRequest("Limit must be between 1 and 100");
        }

        var userId = _currentUser.GetUserIdOrThrow();

        // Kiểm tra quyền theo cơ chế Phân tầng (Dispatcher)
        var roomMeta = await _roomMetadataCache.GetRoomMetadataAsync(roomId);
        if (roomMeta == null) return NotFound("Room not found");

        bool isMember = false;
        if (roomMeta.Value.GroupId.HasValue && !roomMeta.Value.IsPrivate)
        {
            // TẦNG 1: Public Channel -> Hỏi Group Cache
            isMember = await _groupPermissionsCache.IsUserInGroupAsync(roomMeta.Value.GroupId.Value, userId);
        }
        else
        {
            // TẦNG 2: Private/DM -> Hỏi Room Cache
            isMember = await _roomPermissionsCache.IsUserInRoomAsync(roomId, userId);
        }

        if (!isMember)
        {
            return Forbid();
        }

        var messages = await _chatService.GetMessagesAsync(
            roomId,
            beforeMessageId,
            limit);
        var hasMore = messages.Count == limit;

        // Trả về kèm một cờ hasMore đơn giản (nếu số lượng lấy về đúng bằng limit thì có thể còn nữa)
        // Lưu ý: Cờ này mang tính tương đối để Frontend quyết định hiển thị nút Loading hay không.
        var response = new 
        {
            Data = messages,
            HasMore = hasMore,
            NextCursor = hasMore && messages.Count > 0
                ? messages[0].Id
                : null
        };

        return Ok(response);
    }

    /// <summary>
    /// [PATCH] /api/v1/chat/rooms/{roomId}/messages/{messageId} - Sua noi dung tin nhan cua chinh nguoi gui.
    /// </summary>
    [HttpPatch("rooms/{roomId:guid}/messages/{messageId}")]
    [ProducesResponseType(typeof(MessageEditedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EditMessage(
        Guid roomId,
        string messageId,
        [FromBody] EditMessageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserIdOrThrow();

        var result = await _messageMutationService.EditMessageAsync(
            userId,
            roomId,
            messageId,
            request,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// [DELETE] /api/v1/chat/rooms/{roomId}/messages/{messageId} - Xoa mem tin nhan voi moi nguoi.
    /// </summary>
    [HttpDelete("rooms/{roomId:guid}/messages/{messageId}")]
    [ProducesResponseType(typeof(MessageDeletedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteMessage(
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserIdOrThrow();

        var result = await _messageMutationService.DeleteMessageAsync(
            userId,
            roomId,
            messageId,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// [PUT] /api/v1/chat/rooms/{roomId}/messages/{messageId}/reactions - Them reaction cua nguoi dung hien tai.
    /// </summary>
    [HttpPut("rooms/{roomId:guid}/messages/{messageId}/reactions")]
    [ProducesResponseType(typeof(MessageReactionUpdatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMessageReaction(
        Guid roomId,
        string messageId,
        [FromBody] MessageReactionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserIdOrThrow();

        var result = await _messageMutationService.AddReactionAsync(
            userId,
            roomId,
            messageId,
            request.Emoji,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// [DELETE] /api/v1/chat/rooms/{roomId}/messages/{messageId}/reactions - Go reaction cua nguoi dung hien tai.
    /// </summary>
    [HttpDelete("rooms/{roomId:guid}/messages/{messageId}/reactions")]
    [ProducesResponseType(typeof(MessageReactionUpdatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMessageReaction(
        Guid roomId,
        string messageId,
        [FromBody] MessageReactionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserIdOrThrow();

        var result = await _messageMutationService.RemoveReactionAsync(
            userId,
            roomId,
            messageId,
            request.Emoji,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// [POST] /api/v1/chat/rooms/{roomId}/messages/{messageId}/pin - Ghim tin nhan trong phong.
    /// </summary>
    [HttpPost("rooms/{roomId:guid}/messages/{messageId}/pin")]
    [ProducesResponseType(typeof(MessagePinnedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PinMessage(
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserIdOrThrow();

        var result = await _messageMutationService.PinMessageAsync(
            userId,
            roomId,
            messageId,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// [DELETE] /api/v1/chat/rooms/{roomId}/messages/{messageId}/pin - Bo ghim tin nhan trong phong.
    /// </summary>
    [HttpDelete("rooms/{roomId:guid}/messages/{messageId}/pin")]
    [ProducesResponseType(typeof(MessageUnpinnedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnpinMessage(
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetUserIdOrThrow();

        var result = await _messageMutationService.UnpinMessageAsync(
            userId,
            roomId,
            messageId,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// [GET] /api/v1/chat/rooms/{roomId}/pins - Lay danh sach tin nhan dang duoc ghim.
    /// </summary>
    [HttpGet("rooms/{roomId:guid}/pins")]
    [ProducesResponseType(typeof(IEnumerable<Message>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPinnedMessages(
        Guid roomId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserIdOrThrow();

        var result = await _messageMutationService.GetPinnedMessagesAsync(
            userId,
            roomId,
            limit,
            cancellationToken);

        return Ok(result);
    }
}
