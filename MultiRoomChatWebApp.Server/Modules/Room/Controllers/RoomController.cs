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

    public RoomController(IRoomService roomService)
    {
        _roomService = roomService;
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
        return Ok(rooms);
    }
    
    /// <summary>
    /// Tạo hoặc lấy phòng Direct Message với một User khác
    /// </summary>
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
                Name = room.Name
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
