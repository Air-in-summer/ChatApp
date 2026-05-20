using MultiRoomChatWebApp.Server.Modules.Voice.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

/// <summary>
/// DTO trả về trạng thái lifecycle của một VoiceSession.
/// DTO này không chứa LiveKit token.
/// </summary>
public class VoiceSessionResponseDto
{
    public required Guid SessionId { get; init; }

    public required VoiceSessionKind Kind { get; init; }

    public required VoiceSessionStatus Status { get; init; }

    public required Guid SourceRoomId { get; init; }

    public required Guid CreatedByUserId { get; init; }

    public required string LiveKitRoomName { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? EndedAt { get; init; }

    public required IReadOnlyList<VoiceSessionParticipantDto> Participants { get; init; }
}
