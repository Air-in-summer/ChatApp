using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

public interface IMessageDeliveryAttachmentResolver
{
    Task<MessageDeliveryAttachmentResult> ResolveAsync(
        MessageAcceptedEventV1 acceptedEvent,
        DateTime deliveryAtUtc,
        CancellationToken cancellationToken);
}
