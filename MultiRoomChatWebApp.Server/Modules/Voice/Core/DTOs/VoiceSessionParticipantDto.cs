using MultiRoomChatWebApp.Server.Modules.Voice.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

/// <summary>
/// DTO mô tả trạng thái một participant trong VoiceSession.
/// </summary>
public class VoiceSessionParticipantDto
{
    public required Guid UserId { get; init; }

    public required VoiceParticipantStatus Status { get; init; }

    public required DateTime InvitedAt { get; init; }

    public DateTime? JoinedAt { get; init; }

    public DateTime? LeftAt { get; init; }
}
