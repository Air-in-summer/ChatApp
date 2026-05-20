using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Room.Core.Events;

/// <summary>
/// Domain Event bắn ra khi có một hoặc nhiều User được thêm vào một phòng Private.
/// </summary>
/// <param name="RoomId">ID của phòng chat</param>
/// <param name="GroupId">ID của Group chứa phòng</param>
/// <param name="AddedUserIds">Danh sách UserIds vừa được thêm</param>
public record UsersAddedToPrivateRoomEvent(
    Guid RoomId, 
    Guid GroupId, 
    IEnumerable<Guid> AddedUserIds) : INotification;
