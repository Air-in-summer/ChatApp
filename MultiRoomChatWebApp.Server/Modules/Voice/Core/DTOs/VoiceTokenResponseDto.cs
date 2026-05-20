namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

/// <summary>
/// DTO trả về cho client sau khi xin Token thành công.
/// Chứa JWT để connect trực tiếp đến LiveKit Server (không qua Backend).
/// </summary>
public class VoiceTokenResponseDto
{
    /// <summary>
    /// LiveKit Access Token (JWT) - Client dùng token này để connect WebSocket đến LiveKit Server.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// URL của LiveKit Server - Client cần biết để mở WebSocket connection.
    /// Ví dụ: "ws://localhost:7880" (dev) hoặc "wss://livekit.example.com" (production).
    /// </summary>
    public required string LiveKitHost { get; init; }

    /// <summary>
    /// Thời điểm LiveKit token hết hạn theo UTC.
    /// Client dùng để lên lịch refresh trước khi token hết hạn.
    /// </summary>
    public required DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// Số giây còn hiệu lực của LiveKit token tại thời điểm backend cấp token.
    /// </summary>
    public required int ExpiresInSeconds { get; init; }
}
