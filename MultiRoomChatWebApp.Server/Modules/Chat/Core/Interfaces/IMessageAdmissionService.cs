using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Giao diện xử lý quá trình kiểm duyệt (Admission) tin nhắn trước khi được đưa vào hàng đợi xử lý.
/// </summary>
public interface IMessageAdmissionService
{
    /// <summary>
    /// Thực hiện các nghiệp vụ kiểm tra tính hợp lệ, quyền hạn và hạn mức (Rate limit) của tin nhắn.
    /// </summary>
    Task<MessageAdmissionResult> AdmitAsync(
        SendMessageCommand request,
        CancellationToken cancellationToken);
}
