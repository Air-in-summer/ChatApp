using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Giao diện hỗ trợ phân giải (Resolve) và lấy thông tin chi tiết các tệp đính kèm khi phân phối tin nhắn.
/// </summary>
public interface IMessageDeliveryAttachmentResolver
{
    /// <summary>
    /// Xử lý lấy thông tin các media (ảnh, video) đính kèm của sự kiện nhắn tin.
    /// </summary>
    Task<MessageDeliveryAttachmentResult> ResolveAsync(
        MessageAcceptedEventV1 acceptedEvent,
        DateTime deliveryAtUtc,
        CancellationToken cancellationToken);
}
