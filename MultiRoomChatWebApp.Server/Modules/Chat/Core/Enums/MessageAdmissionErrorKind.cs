namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

/// <summary>
/// Nhom loi khi tiep nhan yeu cau gui tin.
/// </summary>
public enum MessageAdmissionErrorKind
{
    Validation = 1,
    Forbidden = 2,
    Conflict = 3,
    BrokerUnavailable = 4
}
