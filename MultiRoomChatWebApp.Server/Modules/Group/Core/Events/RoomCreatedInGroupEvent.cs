using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Sự kiện xảy ra khi một Channel (Room) mới được tạo bên trong một Server (Group).
/// Được sử dụng để kích hoạt Notification đẩy về Client nhằm đồng bộ giao diện.
/// </summary>
public record RoomCreatedInGroupEvent(
    Guid GroupId,
    Guid RoomId,
    string RoomName,
    IEnumerable<Guid> MemberIds) : INotification;
