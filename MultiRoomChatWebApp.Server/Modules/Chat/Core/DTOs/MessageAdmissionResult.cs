namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Kết quả của quá trình kiểm duyệt (Admission) tin nhắn.
/// </summary>
/// <param name="Context">Ngữ cảnh tin nhắn nếu kiểm duyệt thành công (null nếu thất bại).</param>
/// <param name="Error">Chi tiết lỗi nếu kiểm duyệt thất bại (null nếu thành công).</param>
public sealed record MessageAdmissionResult(
    MessageAdmissionContext? Context,
    MessageAdmissionError? Error)
{
    public bool IsAccepted => Context is not null && Error is null;

    public static MessageAdmissionResult Accepted(MessageAdmissionContext context)
        => new(context, null);

    public static MessageAdmissionResult Rejected(MessageAdmissionError error)
        => new(null, error);
}
