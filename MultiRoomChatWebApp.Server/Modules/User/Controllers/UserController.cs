using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.User.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IUserService _userService;

    public UserController(ICurrentUserAccessor currentUser, IUserService userService)
    {
        _currentUser = currentUser;
        _userService = userService;
    }

    /// <summary>
    /// [GET] /api/v1/users/me - Lấy profile của user đang đăng nhập.
    /// </summary>
    /// <returns>UserProfileDto của user hiện tại.</returns>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile()
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            throw ApiException.Unauthorized("user_context_missing", "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");

        var profile = await _userService.GetProfileAsync(currentUserId);
        return Ok(profile);
    }

    /// <summary>
    /// [PUT] /api/v1/users/me/profile - Cập nhật displayName/avatarUrl của user đang đăng nhập.
    /// </summary>
    /// <param name="request">DisplayName và AvatarUrl mới.</param>
    /// <returns>UserProfileDto sau khi cập nhật.</returns>
    [HttpPut("me/profile")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateUserProfileRequest request)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            throw ApiException.Unauthorized("user_context_missing", "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");

        var profile = await _userService.UpdateProfileAsync(currentUserId, request);
        return Ok(profile);
    }

    /// <summary>
    /// [PUT] /api/v1/users/me/password - Đổi mật khẩu tài khoản local.
    /// </summary>
    /// <param name="request">Mật khẩu hiện tại và mật khẩu mới.</param>
    /// <returns>200 OK nếu đổi mật khẩu thành công.</returns>
    [HttpPut("me/password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordRequest request)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            throw ApiException.Unauthorized("user_context_missing", "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");

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
            throw ApiException.Unauthorized("user_context_missing", "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");

        // Validate keyword tối thiểu 1 ký tự
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 1)
            throw ApiException.BadRequest("invalid_search_keyword", "Từ khóa tìm kiếm không được để trống.");

        var results = await _userService.SearchByKeywordAsync(keyword, currentUserId);
        return Ok(results);
    }

    private bool TryGetCurrentUserId(out Guid currentUserId)
    {
        return _currentUser.TryGetUserId(out currentUserId);
    }
}
