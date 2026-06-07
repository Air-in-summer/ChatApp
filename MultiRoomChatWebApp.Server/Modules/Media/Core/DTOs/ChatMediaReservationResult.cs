namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Ket qua giu cho mot tap attachment cho mot tin nhan.
/// </summary>
public sealed record ChatMediaReservationResult(
    bool IsSuccess,
    bool IsNewReservation,
    string? RejectionReason)
{
    public static ChatMediaReservationResult Success(bool isNewReservation)
        => new(true, isNewReservation, null);

    public static ChatMediaReservationResult Rejected(string reason)
        => new(false, false, reason);
}
