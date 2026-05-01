using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Group.Controllers;

[ApiController]
[Route("api/v1/groups")]
[Authorize]
public class GroupController : ControllerBase
{
    private readonly IGroupService _groupService;

    public GroupController(IGroupService groupService)
    {
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
    }

    /// <summary>
    /// [POST] /api/v1/groups - Khởi tạo một Server (Group) mới
    /// </summary>
    /// <remarks>
    /// Request:
    /// - Body: CreateGroupRequest { name: string, description: string, iconUrl: string }
    /// - Headers: Authorization: Bearer {JWT}
    /// 
    /// Response Success (201):
    /// {
    ///   "id": "guid",
    ///   "name": "string",
    ///   "description": "string",
    ///   "iconUrl": "string",
    ///   "inviteCode": "string",
    ///   "ownerId": "guid",
    ///   "createdAt": "datetime"
    /// }
    /// 
    /// Response Error:
    /// - 400: Validation fail (Tên không được bỏ trống)
    /// - 401: Token không hợp lệ hoặc hết hạn
    /// 
    /// Side effects:
    /// - Tạo ra một Server mới trong Database
    /// - Tự động gán quyền Owner cho người gửi request
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        // Validate đơn giản
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Lỗi xác thực",
                Detail = "Tên Server không được bỏ trống."
            });
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized();
        }

        var groupDto = await _groupService.CreateGroupAsync(userId, request);

        // Trả về 201 Created cùng với resource vừa tạo
        return CreatedAtAction(nameof(CreateGroup), new { id = groupDto.Id }, groupDto);
    }

    /// <summary>
    /// [POST] /api/v1/groups/{id}/rooms - Tạo một Channel mới trong Server
    /// </summary>
    /// <remarks>
    /// Request:
    /// - Body: CreateGroupChannelRequest { name: string, type: "Text" | "Voice", isPrivate: boolean }
    /// - Headers: Authorization: Bearer {JWT}
    /// 
    /// Response Success (201):
    /// { "roomId": "guid" }
    /// 
    /// Response Error:
    /// - 400: Validation fail (Tên không được bỏ trống)
    /// - 401: Token không hợp lệ
    /// - 403: Không có quyền tạo kênh (không phải Admin/Owner)
    /// - 404: Không tìm thấy Server
    /// </remarks>
    [HttpPost("{id}/rooms")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateGroupChannel(Guid id, [FromBody] CreateGroupChannelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Lỗi xác thực",
                Detail = "Tên Channel không được bỏ trống."
            });
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized();
        }

        try
        {
            var roomId = await _groupService.CreateGroupChannelAsync(userId, id, request);
            return StatusCode(StatusCodes.Status201Created, new { roomId });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Không có quyền truy cập",
                Detail = ex.Message
            });
        }
    }

    /// <summary>
    /// [POST] /api/v1/groups/join/{inviteCode} - Tham gia một Server thông qua mã mời
    /// </summary>
    /// <remarks>
    /// Request:
    /// - Headers: Authorization: Bearer {JWT}
    /// 
    /// Response Success (200):
    /// { "id": "guid", "name": "string", ... }
    /// 
    /// Response Error:
    /// - 401: Token không hợp lệ
    /// - 404: Không tìm thấy Server với mã mời này
    /// </remarks>
    [HttpPost("join/{inviteCode}")]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> JoinGroup(string inviteCode)
    {
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Lỗi xác thực",
                Detail = "Mã mời không được bỏ trống."
            });
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized();
        }

        try
        {
            var groupDto = await _groupService.JoinGroupByInviteCodeAsync(userId, inviteCode);
            return Ok(groupDto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy Server",
                Detail = ex.Message
            });
        }
    }
}
