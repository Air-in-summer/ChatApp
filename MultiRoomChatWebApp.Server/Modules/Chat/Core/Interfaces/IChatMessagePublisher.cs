using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

public interface IChatMessagePublisher
{
    Task<ChatMessagePublishResult> PublishAsync(
        MessageAcceptedEventV1 acceptedEvent,
        CancellationToken cancellationToken);
}
