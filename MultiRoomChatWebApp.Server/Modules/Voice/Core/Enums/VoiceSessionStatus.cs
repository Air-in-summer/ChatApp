namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Enums;

/// <summary>
/// Trạng thái lifecycle của một phiên media.
/// </summary>
public enum VoiceSessionStatus
{
    Ringing = 0,
    Active = 1,
    Ended = 2,
    Declined = 3,
    Missed = 4
}
