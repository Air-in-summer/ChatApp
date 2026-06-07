using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

public interface IMessageDeliveryRecipientResolver
{
    Task<MessageRecipientResolutionResult> ResolveAsync(
        Guid roomId,
        Guid senderId,
        CancellationToken cancellationToken);
}
