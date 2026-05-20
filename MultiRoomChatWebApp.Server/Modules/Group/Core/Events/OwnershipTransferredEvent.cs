using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Sự kiện xảy ra khi quyền sở hữu Server (Owner) được chuyển nhượng.
/// </summary>
public record OwnershipTransferredEvent(
    Guid GroupId, 
    Guid OldOwnerId, 
    Guid NewOwnerId) : INotification;
