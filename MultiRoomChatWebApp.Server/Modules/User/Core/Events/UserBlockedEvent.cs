using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Events;

/// <summary>
/// Domain event noi bo duoc phat sau khi quan he block giua hai user da duoc ghi nhan.
/// </summary>
public record UserBlockedEvent(
    Guid BlockerId,
    Guid BlockedUserId) : INotification;
