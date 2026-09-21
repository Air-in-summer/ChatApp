namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Kết quả đưa tin nhắn vào hàng đợi lỗi (Dead Letter).
/// </summary>
/// <param name="IsDeadLettered">Xác định xem tin nhắn đã được đẩy vào hàng đợi lỗi thành công hay chưa.</param>
/// <param name="DeadLetterEntryId">Định danh duy nhất của mục lỗi trong hàng đợi Dead Letter (nếu có).</param>
/// <param name="Attempt">Số lần đã thử xử lý trước khi bị chuyển vào Dead Letter.</param>
/// <param name="AcknowledgedCount">Số lượng node/worker đã phản hồi xác nhận quá trình này.</param>
public sealed record ChatBrokerDeadLetterResult(
    bool IsDeadLettered,
    string? DeadLetterEntryId,
    int Attempt,
    long AcknowledgedCount);
