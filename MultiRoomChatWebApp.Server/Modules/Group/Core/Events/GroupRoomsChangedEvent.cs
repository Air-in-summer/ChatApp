using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Su kien bao danh sach phong trong group da thay doi va client can tai lai.
/// </summary>
public record GroupRoomsChangedEvent(
    Guid GroupId,
    IEnumerable<Guid> MemberIds) : INotification;
