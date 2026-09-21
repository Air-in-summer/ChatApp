using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Giao diện phân giải danh sách người nhận (Recipients) của một tin nhắn.
/// </summary>
public interface IMessageDeliveryRecipientResolver
{
    /// <summary>
    /// Lấy danh sách các tài khoản được phép nhận tin nhắn trong một phòng chat.
    /// </summary>
    Task<MessageRecipientResolutionResult> ResolveAsync(
        Guid roomId,
        Guid senderId,
        CancellationToken cancellationToken);
}
