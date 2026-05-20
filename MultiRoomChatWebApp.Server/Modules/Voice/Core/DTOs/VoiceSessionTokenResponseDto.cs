namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

/// <summary>
/// DTO trả về khi một participant được cấp LiveKit token cho VoiceSession.
/// Dùng cho start, accept, reconnect và refresh token của DM call.
/// </summary>
public class VoiceSessionTokenResponseDto
{
    public required VoiceSessionResponseDto Session { get; init; }

    public required string Token { get; init; }

    public required string LiveKitHost { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }

    public required int ExpiresInSeconds { get; init; }
}
