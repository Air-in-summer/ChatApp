using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Thông tin đính kèm khi phân phối tin nhắn.
/// </summary>
/// <param name="IsSuccess">Xác định việc lấy thông tin đính kèm có thành công hay không.</param>
/// <param name="Attachments">Danh sách các tệp đính kèm đã được xác thực và lấy ra thành công.</param>
/// <param name="ErrorCode">Mã lỗi nếu thất bại (ví dụ: media_not_found).</param>
/// <param name="ErrorReason">Lý do thất bại chi tiết.</param>
public sealed record MessageDeliveryAttachmentResult(
    bool IsSuccess,
    IReadOnlyList<Attachment> Attachments,
    string? ErrorCode,
    string? ErrorReason)
{
    public static MessageDeliveryAttachmentResult Resolved(
        IReadOnlyList<Attachment> attachments)
        => new(true, attachments, null, null);

    public static MessageDeliveryAttachmentResult Failed(
        string errorCode,
        string errorReason)
        => new(false, [], errorCode, errorReason);
}
