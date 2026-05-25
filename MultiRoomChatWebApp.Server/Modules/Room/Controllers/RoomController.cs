using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Room.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace MultiRoomChatWebApp.Server.Modules.Room.Controllers;

[ApiController]
[Route("api/v1/rooms")]
[Authorize]
public class RoomController : ControllerBase
{
    private readonly IRoomService _roomService;
    private readonly MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces.IChatService _chatService;

    public RoomController(IRoomService roomService, MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces.IChatService chatService)
    {
        _roomService = roomService;
        _chatService = chatService;
    }

    /// <summary>
    /// Lấy danh sách các phòng của người dùng hiện tại
    /// </summary>
    [HttpGet("my-rooms")]
    [ProducesResponseType(typeof(IEnumerable<RoomDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyRooms()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized("User context is missing");
        }

        var rooms = await _roomService.GetMyRoomsAsync(userId);
        
        // Cần đắp thêm thông tin LastMessage và UnreadCount từ MongoDB
        var roomIds = rooms.Select(r => r.Id).ToList();
        var overviews = await _chatService.GetRoomOverviewsAsync(userId, roomIds);

        foreach (var room in rooms)
        {
            if (overviews.TryGetValue(room.Id, out var overview))
            {
                room.UnreadCount = overview.UnreadCount;
                room.LastReadMessageId = overview.LastReadMessageId;
                if (overview.LastMessage != null)
                {
                    // Lấy preview tin nhắn cuối
                    string content = overview.LastMessage.Content ?? "";
                    if (overview.LastMessage.Attachments != null && overview.LastMessage.Attachments.Any())
                    {
                        content = "[Tệp đính kèm] " + content;
                    }
                    
                    // Nếu là tin nhắn do chính mình gửi, thêm tiền tố "Bạn: "
                    if (overview.LastMessage.SenderId == userId)
                    {
                        content = "Bạn: " + content;
                    }

                    room.LastMessageContent = content;
                    room.LastMessageTimestamp = overview.LastMessage.CreatedAt;
                }
            }
        }

        // Sắp xếp các phòng: phòng nào có tin nhắn mới nhất lên đầu, phòng chưa có tin nhắn ở dưới cùng
        var sortedRooms = rooms.OrderByDescending(r => r.LastMessageTimestamp ?? DateTime.MinValue).ToList();

        return Ok(sortedRooms);
    }
    
    /// <summary>
    /// [POST] /api/v1/rooms/direct/{targetUserId} - Lấy hoặc tạo phòng Direct Message (DM)
    /// </summary>
    /// <param name="targetUserId">Id của người dùng đối diện (User B)</param>
    /// <returns>RoomDto (Chỉ chứa thông tin cơ bản của phòng, Frontend tự đắp tên User B vào)</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Lấy Id của người dùng hiện tại từ JWT Token.
    /// 2. Gọi RoomService để tìm phòng DM chung giữa 2 người, nếu chưa có thì tạo mới.
    /// 3. Map sang RoomDto. (Lưu ý: Không query DB để lấy tên User B nhằm tối ưu hiệu năng, vì Frontend đã có sẵn tên từ bước Search).
    /// </remarks>
    [HttpPost("direct/{targetUserId:guid}")]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrCreateDirectRoom(Guid targetUserId)
    {
        var currentUserIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(currentUserIdString) || !Guid.TryParse(currentUserIdString, out Guid currentUserId))
        {
            return Unauthorized("User context is missing");
        }

        try
        {
            var room = await _roomService.GetOrCreateDirectRoomAsync(currentUserId, targetUserId);
            
            // Map sang DTO để tránh circular reference khi serialize navigation properties
            var dto = new RoomDto
            {
                Id = room.Id,
                Type = room.Type,
                Name = room.Name,
                OtherUserId = targetUserId
            };
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
