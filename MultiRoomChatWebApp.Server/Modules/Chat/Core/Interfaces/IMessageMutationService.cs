using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Xu ly cac thao tac thay doi tren tin nhan da duoc luu trong MongoDB.
/// </summary>
public interface IMessageMutationService
{
    Task<MessageEditedDto> EditMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        EditMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<MessageDeletedDto> DeleteMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken = default);

    Task<MessageReactionUpdatedDto> AddReactionAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default);

    Task<MessageReactionUpdatedDto> RemoveReactionAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default);

    Task<MessagePinnedDto> PinMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken = default);

    Task<MessageUnpinnedDto> UnpinMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> GetPinnedMessagesAsync(
        Guid actorId,
        Guid roomId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
