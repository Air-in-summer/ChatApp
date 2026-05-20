using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Sự kiện xảy ra khi một thành viên tự rời khỏi Group.
/// </summary>
public record MemberLeftGroupEvent(Guid GroupId, Guid UserId) : INotification;
