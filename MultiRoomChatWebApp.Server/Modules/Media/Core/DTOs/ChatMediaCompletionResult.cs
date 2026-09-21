namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Kết quả của quá trình hoàn tất (Completion) việc đính kèm Media vào tin nhắn chính thức.
/// </summary>
/// <param name="IsSuccess">Xác định xem quá trình hoàn tất có thành công hay không.</param>
/// <param name="IsNoOp">Báo hiệu trạng thái không cần làm gì (No-Op), ví dụ khi Media đã được hoàn tất từ trước.</param>
/// <param name="ConflictCode">Mã lỗi nếu xảy ra xung đột (ví dụ: bị thiết bị/node khác hoàn tất mất, hoặc tin nhắn không khớp).</param>
/// <param name="ConflictReason">Lý do chi tiết khi xảy ra xung đột.</param>
public sealed record ChatMediaCompletionResult(
    bool IsSuccess,
    bool IsNoOp,
    string? ConflictCode,
    string? ConflictReason)
{
    public static ChatMediaCompletionResult Completed(bool isNoOp)
        => new(true, isNoOp, null, null);

    public static ChatMediaCompletionResult Conflict(string code, string reason)
        => new(false, false, code, reason);
}