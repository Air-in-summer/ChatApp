using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Controllers;

/// <summary>
/// Controller quản lý lifecycle của VoiceSession cho DM call.
/// Không dùng controller này cho voice room channel cố định.
/// </summary>
/// <remarks>
/// Voice room channel tiếp tục dùng <c>VoiceController</c> và endpoint token theo roomId.
/// DM call dùng controller này để start, accept, decline, refresh token và leave call.
/// </remarks>
[ApiController]
[Route("api/v1/voice/sessions")]
[Authorize]
public class VoiceSessionController : ControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IVoiceSessionService _voiceSessionService;
    private readonly ILogger<VoiceSessionController> _logger;

    public VoiceSessionController(
        ICurrentUserAccessor currentUser,
        IVoiceSessionService voiceSessionService,
        ILogger<VoiceSessionController> logger)
    {
        _currentUser = currentUser;
        _voiceSessionService = voiceSessionService;
        _logger = logger;
    }

    /// <summary>
    /// [POST] /api/v1/voice/sessions/direct/{dmRoomId}/start - Bắt đầu một DM call.
    /// </summary>
    /// <param name="dmRoomId">ID của DirectMessage room chứa caller và callee.</param>
    /// <returns>VoiceSessionTokenResponseDto chứa session lifecycle và LiveKit token cho caller.</returns>
    /// <remarks>
    /// Request:
    /// - Route Param: dmRoomId (Guid) - ID phòng DirectMessage.
    /// - Credential: BFF session cookie hoặc Bearer fallback trong giai đoạn migration.
    ///
    /// Response Success (201):
    /// - Tạo Mongo document trong collection `voice_sessions`.
    /// - Caller được set `Joined`.
    /// - Callee được set `Invited`.
    /// - Trả LiveKit token cho caller để join room `call:{sessionId}`.
    ///
    /// Response Error:
    /// - 400: DM room không đúng 2 thành viên.
    /// - 401: Session đăng nhập không hợp lệ hoặc thiếu user context.
    /// - 403: Caller không phải member của DM room.
    /// - 404: DirectMessage room không tồn tại.
    /// - 409: DM room đang có call chưa kết thúc.
    /// </remarks>
    [HttpPost("direct/{dmRoomId:guid}/start")]
    [ProducesResponseType(typeof(VoiceSessionTokenResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartDirectCall(Guid dmRoomId)
    {
        if (!TryGetCurrentUser(out var userId, out var displayName))
        {
            return Unauthorized("User context is missing");
        }

        try
        {
            var response = await _voiceSessionService.StartDirectCallAsync(dmRoomId, userId, displayName);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (Exception ex) when (IsHandledVoiceSessionException(ex))
        {
            return HandleVoiceSessionException(ex);
        }
    }

    /// <summary>
    /// [POST] /api/v1/voice/sessions/{sessionId}/accept - Accept một DM call đang ringing.
    /// </summary>
    /// <param name="sessionId">ID của VoiceSession cần accept.</param>
    /// <returns>VoiceSessionTokenResponseDto chứa session lifecycle và LiveKit token cho callee.</returns>
    /// <remarks>
    /// Request:
    /// - Route Param: sessionId (Guid) - ID session trong MongoDB.
    /// - Credential: BFF session cookie hoặc Bearer fallback trong giai đoạn migration.
    ///
    /// Response Success (200):
    /// - Chỉ participant đang `Invited` được accept.
    /// - Participant chuyển sang `Joined`.
    /// - Session chuyển từ `Ringing` sang `Active`.
    /// - Trả LiveKit token cho callee để join room `call:{sessionId}`.
    ///
    /// Response Error:
    /// - 401: Session đăng nhập không hợp lệ hoặc thiếu user context.
    /// - 403: User không phải participant được mời.
    /// - 404: VoiceSession không tồn tại.
    /// - 409: Session không còn ở trạng thái Ringing.
    /// </remarks>
    [HttpPost("{sessionId:guid}/accept")]
    [ProducesResponseType(typeof(VoiceSessionTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(Guid sessionId)
    {
        if (!TryGetCurrentUser(out var userId, out var displayName))
        {
            return Unauthorized("User context is missing");
        }

        try
        {
            return Ok(await _voiceSessionService.AcceptAsync(sessionId, userId, displayName));
        }
        catch (Exception ex) when (IsHandledVoiceSessionException(ex))
        {
            return HandleVoiceSessionException(ex);
        }
    }

    /// <summary>
    /// [POST] /api/v1/voice/sessions/{sessionId}/decline - Từ chối một DM call đang ringing.
    /// </summary>
    /// <param name="sessionId">ID của VoiceSession cần decline.</param>
    /// <returns>VoiceSessionResponseDto sau khi session chuyển sang Declined.</returns>
    /// <remarks>
    /// Request:
    /// - Route Param: sessionId (Guid) - ID session trong MongoDB.
    /// - Credential: BFF session cookie hoặc Bearer fallback trong giai đoạn migration.
    ///
    /// Response Success (200):
    /// - Chỉ participant đang `Invited` được decline.
    /// - Participant chuyển sang `Declined`.
    /// - Session chuyển sang `Declined` và set `EndedAt`.
    /// - Không trả LiveKit token.
    ///
    /// Response Error:
    /// - 401: Session đăng nhập không hợp lệ hoặc thiếu user context.
    /// - 403: User không phải participant được mời.
    /// - 404: VoiceSession không tồn tại.
    /// - 409: Session không còn ở trạng thái Ringing.
    /// </remarks>
    [HttpPost("{sessionId:guid}/decline")]
    [ProducesResponseType(typeof(VoiceSessionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decline(Guid sessionId)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return Unauthorized("User context is missing");
        }

        try
        {
            return Ok(await _voiceSessionService.DeclineAsync(sessionId, userId));
        }
        catch (Exception ex) when (IsHandledVoiceSessionException(ex))
        {
            return HandleVoiceSessionException(ex);
        }
    }

    /// <summary>
    /// [POST] /api/v1/voice/sessions/{sessionId}/token - Lấy lại LiveKit token cho DM call.
    /// </summary>
    /// <param name="sessionId">ID của VoiceSession cần cấp token.</param>
    /// <returns>VoiceSessionTokenResponseDto chứa session lifecycle và LiveKit token mới.</returns>
    /// <remarks>
    /// Request:
    /// - Route Param: sessionId (Guid) - ID session trong MongoDB.
    /// - Credential: BFF session cookie hoặc Bearer fallback trong giai đoạn migration.
    ///
    /// Dùng cho:
    /// - Refresh token trước khi hết hạn.
    /// - Reconnect sau reload.
    ///
    /// Response Success (200):
    /// - Chỉ participant đang `Joined` được cấp token.
    /// - Không thay đổi lifecycle chính của session.
    ///
    /// Response Error:
    /// - 401: Session đăng nhập không hợp lệ hoặc thiếu user context.
    /// - 403: User không phải participant joined.
    /// - 404: VoiceSession không tồn tại.
    /// - 409: Session đã kết thúc hoặc không còn hợp lệ để cấp token.
    /// </remarks>
    [HttpPost("{sessionId:guid}/token")]
    [ProducesResponseType(typeof(VoiceSessionTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetToken(Guid sessionId)
    {
        if (!TryGetCurrentUser(out var userId, out var displayName))
        {
            return Unauthorized("User context is missing");
        }

        try
        {
            return Ok(await _voiceSessionService.GetTokenAsync(sessionId, userId, displayName));
        }
        catch (Exception ex) when (IsHandledVoiceSessionException(ex))
        {
            return HandleVoiceSessionException(ex);
        }
    }

    /// <summary>
    /// [POST] /api/v1/voice/sessions/{sessionId}/leave - Rời khỏi một DM call.
    /// </summary>
    /// <param name="sessionId">ID của VoiceSession cần rời.</param>
    /// <returns>VoiceSessionResponseDto sau khi cập nhật participant/session lifecycle.</returns>
    /// <remarks>
    /// Request:
    /// - Route Param: sessionId (Guid) - ID session trong MongoDB.
    /// - Credential: BFF session cookie hoặc Bearer fallback trong giai đoạn migration.
    ///
    /// Response Success (200):
    /// - Participant đang `Joined` chuyển sang `Left`.
    /// - Nếu không còn participant `Joined`, session chuyển sang `Ended`.
    /// - Không trả LiveKit token.
    ///
    /// Response Error:
    /// - 401: Session đăng nhập không hợp lệ hoặc thiếu user context.
    /// - 403: User không phải participant của session.
    /// - 404: VoiceSession không tồn tại.
    /// - 409: Participant chưa joined hoặc session không hợp lệ để leave.
    /// </remarks>
    [HttpPost("{sessionId:guid}/leave")]
    [ProducesResponseType(typeof(VoiceSessionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Leave(Guid sessionId)
    {
        if (!TryGetCurrentUser(out var userId, out _))
        {
            return Unauthorized("User context is missing");
        }

        try
        {
            return Ok(await _voiceSessionService.LeaveAsync(sessionId, userId));
        }
        catch (Exception ex) when (IsHandledVoiceSessionException(ex))
        {
            return HandleVoiceSessionException(ex);
        }
    }

    private bool TryGetCurrentUser(out Guid userId, out string displayName)
    {
        var hasUserId = _currentUser.TryGetUserId(out userId);
        displayName = hasUserId
            ? _currentUser.GetDisplayName(userId)
            : "Unknown";

        return hasUserId;
    }

    private static bool IsHandledVoiceSessionException(Exception ex)
    {
        return ex is KeyNotFoundException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException;
    }

    private IActionResult HandleVoiceSessionException(Exception ex)
    {
        var (status, title) = ex switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Không tìm thấy"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Không có quyền"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Request không hợp lệ"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Trạng thái không hợp lệ"),
            _ => (StatusCodes.Status500InternalServerError, "Lỗi không xác định")
        };

        _logger.LogWarning(ex, "VoiceSession request failed with status {StatusCode}", status);

        return StatusCode(status, new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = ex.Message
        });
    }
}
