using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.Events;

/// <summary>
/// Sự kiện xảy ra khi một người dùng tham gia vào Group (Server).
/// </summary>
/// <param name="GroupId">ID của Server</param>
/// <param name="UserId">ID của người dùng vừa tham gia</param>
public record MemberJoinedGroupEvent(Guid GroupId, Guid UserId) : INotification;
