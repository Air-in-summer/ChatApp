namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

/// <summary>
/// Payload SignalR gửi khi trạng thái DM call thay đổi.
/// </summary>
public class VoiceCallStatusChangedDto
{
    public required VoiceSessionResponseDto Session { get; init; }

    public required Guid ActorUserId { get; init; }
}
