using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using System.Security.Claims;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Controllers;

[ApiController]
[Route("api/v1/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IRoomPermissionsCache _roomPermissionsCache;

    public ChatController(IChatService chatService, IRoomPermissionsCache roomPermissionsCache)
    {
        _chatService = chatService;
        _roomPermissionsCache = roomPermissionsCache;
    }

    /// <summary>
    /// Lấy lịch sử tin nhắn của một phòng với tính năng Lazy Load (Cursor-based Pagination)
    /// </summary>
    /// <param name="roomId">ID của phòng</param>
    /// <param name="cursor">ID của tin nhắn cũ nhất đang hiện trên màn hình (để lấy tin cũ hơn)</param>
    /// <param name="limit">Số lượng lấy</param>
    [HttpGet("rooms/{roomId:guid}/messages")]
    [ProducesResponseType(typeof(IEnumerable<Message>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMessages(Guid roomId, [FromQuery] string? cursor = null, [FromQuery] int limit = 50)
    {
        if (limit <= 0 || limit > 100)
        {
            return BadRequest("Limit must be between 1 and 100");
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized("User context is missing");
        }

        // Kiểm tra quyền: Chỉ thành viên phòng mới được xem tin nhắn
        bool isMember = await _roomPermissionsCache.IsUserInRoomAsync(roomId, userId);
        if (!isMember)
        {
            return Forbid();
        }

        var messages = await _chatService.GetMessagesAsync(roomId, cursor, limit);

        // Trả về kèm một cờ hasMore đơn giản (nếu số lượng lấy về đúng bằng limit thì có thể còn nữa)
        // Lưu ý: Cờ này mang tính tương đối để Frontend quyết định hiển thị nút Loading hay không.
        var response = new 
        {
            Data = messages,
            HasMore = messages.Count() == limit
        };

        return Ok(response);
    }
}
