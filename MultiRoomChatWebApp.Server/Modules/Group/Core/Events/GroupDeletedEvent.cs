using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Sự kiện xảy ra khi một Server bị giải tán (Soft Delete).
/// </summary>
public record GroupDeletedEvent(
    Guid GroupId, 
    Guid DeletedByUserId, 
    IEnumerable<Guid> MemberIds) : INotification;
