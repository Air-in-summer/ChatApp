namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Enums;

/// <summary>
/// Trạng thái của từng participant trong một VoiceSession.
/// </summary>
public enum VoiceParticipantStatus
{
    Invited = 0,
    Joined = 1,
    Left = 2,
    Declined = 3
}
