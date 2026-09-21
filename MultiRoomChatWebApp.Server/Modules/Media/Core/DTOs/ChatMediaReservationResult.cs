namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Kết quả của việc giữ chỗ (Reservation) một tệp đính kèm chuẩn bị cho một tin nhắn sẽ được gửi.
/// </summary>
/// <param name="IsSuccess">Xác định việc giữ chỗ có thành công hay không.</param>
/// <param name="IsNewReservation">Cờ xác định đây là một lượt giữ chỗ mới hoàn toàn hay là tái giữ chỗ (đã giữ trước đó).</param>
/// <param name="RejectionReason">Lý do từ chối nếu việc giữ chỗ thất bại (ví dụ: đã bị gán cho tin nhắn khác).</param>
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