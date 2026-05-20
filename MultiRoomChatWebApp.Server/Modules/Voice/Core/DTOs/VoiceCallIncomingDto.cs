namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

/// <summary>
/// Payload SignalR gửi tới callee khi có DM call mới.
/// </summary>
public class VoiceCallIncomingDto
{
    public required VoiceSessionResponseDto Session { get; init; }

    public required Guid CallerId { get; init; }

    public required string CallerDisplayName { get; init; }
}
