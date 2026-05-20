using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;
using System.Security.Claims;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Controllers;

/// <summary>
/// Controller quản lý Voice Module.
/// Nhiệm vụ DUY NHẤT: Kiểm tra quyền thành viên → Cấp LiveKit Access Token.
/// Toàn bộ media routing (audio/video/screen) do LiveKit Server xử lý.
/// </summary>
[ApiController]
[Route("api/v1/voice")]
[Authorize]
public class VoiceController : ControllerBase
{
    private readonly IVoiceTokenService _voiceTokenService;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        IVoiceTokenService voiceTokenService,
        IRoomPermissionsCache roomPermissionsCache,
        IRoomMetadataCache roomMetadataCache,
        ILogger<VoiceController> logger)
    {
        _voiceTokenService = voiceTokenService;
        _roomPermissionsCache = roomPermissionsCache;
        _roomMetadataCache = roomMetadataCache;
        _logger = logger;
    }

    /// <summary>
    /// [POST] /api/v1/voice/token - Lấy LiveKit Access Token để tham gia phòng Voice
    /// </summary>
    /// <param name="roomId">ID của phòng Voice (phải là RoomType.Voice = 1)</param>
    /// <returns>VoiceTokenResponseDto chứa JWT token + LiveKit host URL</returns>
    /// <remarks>
    /// Request:
    /// - Route Param: roomId (Guid) - ID phòng Voice
    /// - Headers: Authorization: Bearer {JWT}
    /// 
    /// Response Success (200):
    /// {
    ///   "token": "eyJhbGciOiJIUzI1NiIs...",
    ///   "liveKitHost": "ws://localhost:7880"
    /// }
    /// 
    /// Response Error:
    /// - 401: Token JWT không hợp lệ hoặc hết hạn
    /// - 403: User không phải thành viên của phòng (hoặc Group chứa phòng)
    /// - 404: Phòng không tồn tại hoặc không phải phòng Voice
    /// 
    /// Luồng xử lý:
    /// 1. Lấy userId từ JWT Claims
    /// 2. Kiểm tra user có quyền truy cập Room (qua RoomPermissionsCache)
    /// 3. Tạo LiveKit Token (VoiceTokenService)
    /// 4. Trả về Token + LiveKit Host URL
    /// 
    /// Side effects: Không có (stateless, chỉ đọc DB/Cache + tạo JWT)
    /// </remarks>
    [HttpPost("token/{roomId:guid}")]
    [ProducesResponseType(typeof(VoiceTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVoiceToken(Guid roomId)
    {
        // ──────────────────────────────────────────────────────
        // Bước 1: Lấy userId từ JWT Claims
        // ──────────────────────────────────────────────────────
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized("User context is missing");
        }

        // Ưu tiên displayName để LiveKit participant không hiện Unknown trong UI voice/call.
        var displayName = GetCurrentUserDisplayName(userId);

        var roomMetadata = await _roomMetadataCache.GetRoomMetadataAsync(roomId);
        if (roomMetadata == null || roomMetadata.Value.Type != RoomType.Voice)
        {
            _logger.LogWarning(
                "User {UserId} yêu cầu Voice Token cho Room {RoomId} không tồn tại hoặc không phải Voice room",
                userId, roomId);
            return NotFound("Voice room not found");
        }

        // ──────────────────────────────────────────────────────
        // Bước 2: Kiểm tra quyền truy cập Room
        // ──────────────────────────────────────────────────────
        // Sử dụng RoomPermissionsCache (Redis → PostgreSQL fallback)
        // Cache check O(1), đảm bảo user là member của Room hoặc Group chứa Room
        var isMember = await _roomPermissionsCache.IsUserInRoomAsync(roomId, userId);
        if (!isMember)
        {
            _logger.LogWarning(
                "User {UserId} bị từ chối truy cập Voice Room {RoomId}: không phải thành viên",
                userId, roomId);
            return Forbid();
        }

        // ──────────────────────────────────────────────────────
        // Bước 3: Tạo LiveKit Token + trả về response
        // ──────────────────────────────────────────────────────
        var token = _voiceTokenService.GenerateToken(roomId, userId, displayName);
        var liveKitHost = _voiceTokenService.GetLiveKitHost();
        var tokenTtl = _voiceTokenService.GetTokenTtl();

        _logger.LogInformation(
            "User {UserId} ({DisplayName}) đã nhận Voice Token cho Room {RoomId}",
            userId, displayName, roomId);

        return Ok(new VoiceTokenResponseDto
        {
            Token = token,
            LiveKitHost = liveKitHost,
            ExpiresAtUtc = DateTime.UtcNow.Add(tokenTtl),
            ExpiresInSeconds = (int)tokenTtl.TotalSeconds
        });
    }

    private string GetCurrentUserDisplayName(Guid userId)
    {
        return User.FindFirstValue("displayName")
            ?? User.FindFirstValue("name")
            ?? userId.ToString();
    }
}
