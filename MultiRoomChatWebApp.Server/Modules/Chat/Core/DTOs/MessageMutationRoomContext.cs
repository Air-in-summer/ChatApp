using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Ngu canh quyen cua phong dung trong cac thao tac thay doi tin nhan.
/// </summary>
public sealed record MessageMutationRoomContext(
    Guid RoomId,
    RoomType RoomType,
    Guid? GroupId,
    bool IsPrivate);
