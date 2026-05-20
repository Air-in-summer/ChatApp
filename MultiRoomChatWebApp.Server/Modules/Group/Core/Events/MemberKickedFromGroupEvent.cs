using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Sự kiện xảy ra khi một thành viên bị trục xuất (Kick) khỏi Group bởi Admin/Owner.
/// </summary>
public record MemberKickedFromGroupEvent(
    Guid GroupId, 
    Guid KickedUserId, 
    Guid KickedByUserId) : INotification;
