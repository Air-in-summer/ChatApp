using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Định nghĩa các phương thức xuất bản (Publish) sự kiện tin nhắn vào hệ thống Message Broker.
/// </summary>
public interface IChatMessagePublisher
{
    /// <summary>
    /// Xuất bản sự kiện tin nhắn đã được hệ thống chấp nhận (Accepted).
    /// </summary>
    Task<ChatMessagePublishResult> PublishAsync(
        MessageAcceptedEventV1 acceptedEvent,
        CancellationToken cancellationToken);
}
