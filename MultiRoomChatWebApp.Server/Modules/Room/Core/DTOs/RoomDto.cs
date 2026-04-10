using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Room.Core.DTOs;

public class RoomDto
{
    public Guid Id { get; set; }
    
    public RoomType Type { get; set; }
    
    public string? Name { get; set; }
    
    public string? OtherUserDisplayName { get; set; }
    
    public string? OtherUserUsername { get; set; }
}
