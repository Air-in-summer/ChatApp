using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed record MessageAdmissionContext(
    Guid RoomId,
    Guid SenderId,
    Guid ClientMessageId,
    string Content,
    IReadOnlyList<Guid> MediaIds,
    Guid? GroupId,
    bool IsPrivateRoom,
    RoomType RoomType);
