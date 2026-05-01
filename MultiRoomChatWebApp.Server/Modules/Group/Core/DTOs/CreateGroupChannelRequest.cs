using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;

public class CreateGroupChannelRequest
{
    public string Name { get; set; } = string.Empty;
    public RoomType Type { get; set; } = RoomType.Text;
    public bool IsPrivate { get; set; } = false;
}
