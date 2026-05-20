using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.User.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// [GET] /api/v1/users/me - Lay profile cua user dang dang nhap.
    /// </summary>
    /// <returns>UserProfileDto cua user hien tai.</returns>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile()
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized("User context is missing");

        var profile = await _userService.GetProfileAsync(currentUserId);
        return Ok(profile);
    }

    /// <summary>
    /// [PUT] /api/v1/users/me/profile - Cap nhat displayName/avatarUrl cua user dang dang nhap.
    /// </summary>
    /// <param name="request">DisplayName va AvatarUrl moi.</param>
    /// <returns>UserProfileDto sau khi cap nhat.</returns>
    [HttpPut("me/profile")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateUserProfileRequest request)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized("User context is missing");

        var profile = await _userService.UpdateProfileAsync(currentUserId, request);
        return Ok(profile);
    }

    /// <summary>
    /// [PUT] /api/v1/users/me/password - Doi mat khau tai khoan local.
    /// </summary>
    /// <param name="request">Mat khau hien tai va mat khau moi.</param>
    /// <returns>200 OK neu doi mat khau thanh cong.</returns>
    [HttpPut("me/password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordRequest request)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized("User context is missing");

        await _userService.ChangePasswordAsync(currentUserId, request);
        return Ok();
    }

    /// <summary>
    /// [GET] /api/v1/users/search?keyword=... - Tìm kiếm người dùng theo Username hoặc DisplayName
    /// </summary>
    /// <param name="keyword">Từ khóa tìm kiếm (tối thiểu 1 ký tự)</param>
    /// <returns>Danh sách tối đa 10 người dùng khớp với từ khóa</returns>
    /// <remarks>
    /// Response Success (200):
    /// [
    ///   { "id": "guid", "username": "imgemini", "displayName": "Gemini AI" },
    ///   ...
    /// ]
    ///
    /// Response Error:
    /// - 400: Keyword rỗng hoặc không hợp lệ.
    /// - 401: Chưa đăng nhập (thiếu JWT token).
    /// </remarks>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IEnumerable<UserSearchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchUsers([FromQuery] string keyword)
    {
        // Parse userId từ JWT claim
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized("User context is missing");

        // Validate keyword tối thiểu 1 ký tự
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 1)
            return BadRequest("Keyword must be at least 1 character.");

        var results = await _userService.SearchByKeywordAsync(keyword, currentUserId);
        return Ok(results);
    }

    private bool TryGetCurrentUserId(out Guid currentUserId)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdString, out currentUserId);
    }
}
