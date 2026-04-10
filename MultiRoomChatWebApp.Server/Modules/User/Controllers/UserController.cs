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
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid currentUserId))
            return Unauthorized("User context is missing");

        // Validate keyword tối thiểu 1 ký tự
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 1)
            return BadRequest("Keyword must be at least 1 character.");

        var results = await _userService.SearchByKeywordAsync(keyword, currentUserId);
        return Ok(results);
    }
}
