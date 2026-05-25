using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.User.Controllers;

/// <summary>
/// Controller quan ly friends, friend requests va blocks cua user hien tai.
/// </summary>
[ApiController]
[Route("api/v1/users/relationships")]
[Authorize]
public class UserRelationshipsController : ControllerBase
{
    private readonly IUserRelationshipService _relationshipService;
    private readonly IUserPresenceService _presenceService;

    public UserRelationshipsController(
        IUserRelationshipService relationshipService,
        IUserPresenceService presenceService)
    {
        _relationshipService = relationshipService;
        _presenceService = presenceService;
    }

    /// <summary>
    /// [GET] /api/v1/users/relationships/friends - Lay danh sach ban be cua user hien tai.
    /// </summary>
    [HttpGet("friends")]
    [ProducesResponseType(typeof(IReadOnlyCollection<FriendDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFriends(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.GetFriendsAsync(currentUserId, cancellationToken));
    }

    /// <summary>
    /// [DELETE] /api/v1/users/relationships/friends/{userId} - Xoa quan he ban be.
    /// </summary>
    [HttpDelete("friends/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFriend(Guid userId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        await _relationshipService.RemoveFriendAsync(currentUserId, userId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// [GET] /api/v1/users/relationships/friend-requests/incoming - Lay loi moi ket ban da nhan.
    /// </summary>
    [HttpGet("friend-requests/incoming")]
    [ProducesResponseType(typeof(IReadOnlyCollection<FriendRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetIncomingFriendRequests(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.GetIncomingFriendRequestsAsync(currentUserId, cancellationToken));
    }

    /// <summary>
    /// [GET] /api/v1/users/relationships/friend-requests/outgoing - Lay loi moi ket ban da gui.
    /// </summary>
    [HttpGet("friend-requests/outgoing")]
    [ProducesResponseType(typeof(IReadOnlyCollection<FriendRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOutgoingFriendRequests(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.GetOutgoingFriendRequestsAsync(currentUserId, cancellationToken));
    }

    /// <summary>
    /// [POST] /api/v1/users/relationships/friend-requests - Tao loi moi ket ban.
    /// </summary>
    [HttpPost("friend-requests")]
    [EnableRateLimiting("FriendRequestLimit")]
    [ProducesResponseType(typeof(FriendRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateFriendRequest(
        [FromBody] CreateFriendRequestRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var result = await _relationshipService.CreateFriendRequestAsync(currentUserId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// [POST] /api/v1/users/relationships/friend-requests/{requestId}/accept - Chap nhan loi moi ket ban.
    /// </summary>
    [HttpPost("friend-requests/{requestId:guid}/accept")]
    [ProducesResponseType(typeof(FriendRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptFriendRequest(Guid requestId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.AcceptFriendRequestAsync(currentUserId, requestId, cancellationToken));
    }

    /// <summary>
    /// [POST] /api/v1/users/relationships/friend-requests/{requestId}/decline - Tu choi loi moi ket ban.
    /// </summary>
    [HttpPost("friend-requests/{requestId:guid}/decline")]
    [ProducesResponseType(typeof(FriendRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineFriendRequest(Guid requestId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.DeclineFriendRequestAsync(currentUserId, requestId, cancellationToken));
    }

    /// <summary>
    /// [POST] /api/v1/users/relationships/friend-requests/{requestId}/cancel - Huy loi moi ket ban da gui.
    /// </summary>
    [HttpPost("friend-requests/{requestId:guid}/cancel")]
    [ProducesResponseType(typeof(FriendRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelFriendRequest(Guid requestId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.CancelFriendRequestAsync(currentUserId, requestId, cancellationToken));
    }

    /// <summary>
    /// [GET] /api/v1/users/relationships/blocks - Lay danh sach user da bi current user chan.
    /// </summary>
    [HttpGet("blocks")]
    [ProducesResponseType(typeof(IReadOnlyCollection<BlockedUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBlockedUsers(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.GetBlockedUsersAsync(currentUserId, cancellationToken));
    }

    /// <summary>
    /// [POST] /api/v1/users/relationships/blocks - Chan mot user.
    /// </summary>
    [HttpPost("blocks")]
    [EnableRateLimiting("BlockActionLimit")]
    [ProducesResponseType(typeof(BlockedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BlockUser([FromBody] BlockUserRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _relationshipService.BlockUserAsync(currentUserId, request, cancellationToken));
    }

    /// <summary>
    /// [DELETE] /api/v1/users/relationships/blocks/{userId} - Bo chan mot user.
    /// </summary>
    [HttpDelete("blocks/{userId:guid}")]
    [EnableRateLimiting("BlockActionLimit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UnblockUser(Guid userId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        await _relationshipService.UnblockUserAsync(currentUserId, userId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// [GET] /api/v1/users/relationships/presence/friends - Lấy snapshot online/lastSeen của friends hợp lệ.
    /// </summary>
    [HttpGet("presence/friends")]
    [ProducesResponseType(typeof(IReadOnlyCollection<PresenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFriendsPresence(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        return Ok(await _presenceService.GetFriendsPresenceAsync(currentUserId, cancellationToken));
    }

    private Guid GetCurrentUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var currentUserId))
            throw ApiException.Unauthorized("user_context_missing", "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");

        return currentUserId;
    }
}
