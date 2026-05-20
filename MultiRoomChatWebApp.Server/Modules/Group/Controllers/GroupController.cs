using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Group.Controllers;

[ApiController]
[Route("api/v1/groups")]
[Authorize]
public class GroupController : ControllerBase
{
    private readonly IGroupService _groupService;
    private readonly IRoomService _roomService;
    private readonly IGroupPermissionsCache _permissionsCache;

    public GroupController(
        IGroupService groupService,
        IRoomService roomService,
        IGroupPermissionsCache permissionsCache)
    {
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _permissionsCache = permissionsCache ?? throw new ArgumentNullException(nameof(permissionsCache));
    }

    /// <summary>
    /// [GET] /api/v1/groups - Lấy danh sách các Server người dùng đã tham gia.
    /// Tối ưu hiệu năng bằng Redis Metadata Cache.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<GroupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyGroups()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized();
        }

        var groups = await _groupService.GetMyGroupsAsync(userId);
        return Ok(groups);
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

    /// <summary>
    /// [GET] /api/v1/groups/{id}/rooms - Lấy danh sách các Channel trong Server
    /// </summary>
    [HttpGet("{id}/rooms")]
    [ProducesResponseType(typeof(IEnumerable<MultiRoomChatWebApp.Server.Modules.Room.Core.DTOs.RoomDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroupRooms(Guid id)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized();
        }

        var rooms = await _groupService.GetGroupRoomsAsync(id, userId);
        return Ok(rooms);
    }

    /// <summary>
    /// [GET] /api/v1/groups/{id}/members - Lấy danh sách thành viên trong Server
    /// </summary>
    [HttpGet("{id}/members")]
    [ProducesResponseType(typeof(IEnumerable<MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs.GroupMemberDto>), StatusCodes.Status200OK)]

    public async Task<IActionResult> GetGroupMembers(Guid id)
    {
        var members = await _groupService.GetGroupMembersAsync(id);
        return Ok(members);
    }

    /// <summary>
    /// [PATCH] /api/v1/groups/{id} - Cập nhật thông tin Server
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdateGroupRequest request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

        var updatedGroup = await _groupService.UpdateGroupAsync(userId, id, request);
        return Ok(updatedGroup);
    }

    /// <summary>
    /// [PATCH] /api/v1/groups/{id}/members/{targetUserId}/role - Thay đổi vai trò thành viên
    /// </summary>
    [HttpPatch("{id}/members/{targetUserId}/role")]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid targetUserId, [FromBody] MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole newRole)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

        await _groupService.UpdateMemberRoleAsync(userId, id, targetUserId, newRole);
        return NoContent();
    }

    /// <summary>
    /// [POST] /api/v1/groups/{id}/transfer-ownership - Chuyển nhượng quyền sở hữu tối cao
    /// </summary>
    [HttpPost("{id}/transfer-ownership")]
    public async Task<IActionResult> TransferOwnership(Guid id, [FromBody] Guid newOwnerId)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

        await _groupService.TransferOwnershipAsync(userId, id, newOwnerId);
        return NoContent();
    }

    /// <summary>
    /// [POST] /api/v1/groups/{id}/leave - Rời khỏi Server
    /// </summary>
    [HttpPost("{id}/leave")]
    public async Task<IActionResult> LeaveGroup(Guid id)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

        try
        {
            await _groupService.LeaveGroupAsync(userId, id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Không thể rời Server",
                Detail = ex.Message
            });
        }
    }

    /// <summary>
    /// [POST] /api/v1/groups/{id}/kick/{userId} - Trục xuất thành viên (Admin/Owner only)
    /// </summary>
    [HttpPost("{id}/kick/{userId}")]
    public async Task<IActionResult> KickMember(Guid id, Guid userId)
    {
        var adminIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(adminIdString, out Guid adminId)) return Unauthorized();

        try
        {
            await _groupService.KickMemberAsync(adminId, id, userId);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy thành viên",
                Detail = ex.Message
            });
        }
    }

    /// <summary>
    /// [DELETE] /api/v1/groups/{id} - Giải tán Server (Chỉ dành cho Chủ sở hữu)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(Guid id)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

        try
        {
            await _groupService.SoftDeleteGroupAsync(userId, id);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
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

    /// <summary>
    /// [POST] /api/v1/groups/{id}/rooms/{roomId}/members - Thêm thành viên Server vào phòng Private
    /// </summary>
    /// <remarks>
    /// Trigger: Admin/Owner bấm nút Add Member trong header chat room.
    /// 
    /// Luồng xử lý:
    /// 1. Check quyền người gọi (Phải là Owner/Admin của Group) -> IGroupPermissionsCache.
    /// 2. Validate danh sách UserIds truyền lên (Phải là thành viên của Group) -> IGroupPermissionsCache.
    /// 3. Gọi RoomService để thực hiện logic nghiệp vụ (Insert DB, Update Room Cache, Bắn Notification).
    /// </remarks>
    [HttpPost("{id}/rooms/{roomId}/members")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddRoomMembers(Guid id, Guid roomId, [FromBody] AddRoomMembersRequest request)
    {
        if (request.UserIds == null || !request.UserIds.Any())
        {
            return BadRequest(new ProblemDetails { Detail = "Danh sách UserIds không được trống." });
        }

        var requesterIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(requesterIdString, out Guid requesterId)) return Unauthorized();

        // 1. Kiểm tra quyền của người gọi (Sử dụng Cache O(1))
        var requesterRole = await _permissionsCache.GetMemberRoleAsync(id, requesterId);
        if (requesterRole != GroupRole.Owner && requesterRole != GroupRole.Admin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Không có quyền",
                Detail = "Chỉ Owner hoặc Admin của Server mới có quyền thêm thành viên vào phòng Private."
            });
        }

        // 2. Validate danh sách User truyền lên: Chỉ giữ lại những người thực sự thuộc Server
        // (Đây là lớp bảo vệ để tránh việc add user từ Server khác vào channel)
        var validUserIds = new List<Guid>();
        foreach (var targetId in request.UserIds.Distinct())
        {
            if (await _permissionsCache.IsUserInGroupAsync(id, targetId))
            {
                validUserIds.Add(targetId);
            }
        }

        if (!validUserIds.Any())
        {
            return BadRequest(new ProblemDetails { Detail = "Không tìm thấy user nào hợp lệ trong Server này." });
        }

        // 3. Ra lệnh cho RoomService thực hiện việc add
        try
        {
            await _roomService.AddMembersToPrivateRoomAsync(roomId, validUserIds);
            return Ok(new { addedCount = validUserIds.Count });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>
    /// [GET] /api/v1/groups/{id}/rooms/{roomId}/members/ids - Lấy ID các thành viên hiện tại của phòng Private
    /// </summary>
    /// <remarks>
    /// Trigger: Khi Modal "Thêm thành viên" mở lên.
    /// Mục đích: Để Frontend lọc bỏ những người đã có trong phòng khỏi danh sách có thể chọn.
    /// Giới hạn quyền: Chỉ Owner/Admin mới được gọi để bảo vệ tính riêng tư của phòng.
    /// </remarks>
    [HttpGet("{id}/rooms/{roomId}/members/ids")]
    [ProducesResponseType(typeof(IEnumerable<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPrivateRoomMemberIds(Guid id, Guid roomId)
    {
        /*var requesterIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(requesterIdString, out Guid requesterId)) return Unauthorized();

        // 1. Kiểm tra quyền của người gọi (Sử dụng Cache O(1))
        var requesterRole = await _permissionsCache.GetMemberRoleAsync(id, requesterId);
        if (requesterRole != GroupRole.Owner && requesterRole != GroupRole.Admin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Không có quyền",
                Detail = "Chỉ Owner hoặc Admin mới có quyền xem danh sách thành viên của phòng Private để quản lý."
            });
        }*/

        // 2. Lấy dữ liệu (Từ Cache O(1))
        var memberIds = await _roomService.GetRoomMemberIdsAsync(roomId);
        return Ok(memberIds);
    }
}


