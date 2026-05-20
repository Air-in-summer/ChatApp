using MediatR;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Sự kiện xảy ra khi vai trò của một thành viên trong Group bị thay đổi.
/// </summary>
public record MemberRoleUpdatedEvent(
    Guid GroupId, 
    Guid TargetUserId, 
    GroupRole OldRole, 
    GroupRole NewRole, 
    Guid UpdatedByUserId) : INotification;
