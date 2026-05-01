using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Room.Core.DTOs;

public class RoomDto
{
    public Guid Id { get; set; }
    
    public RoomType Type { get; set; }
    
    public string? Name { get; set; }
    
    public string? OtherUserDisplayName { get; set; }
    
    public string? OtherUserUsername { get; set; }

    public string? LastMessageContent { get; set; }

    public DateTime? LastMessageTimestamp { get; set; }

    public int UnreadCount { get; set; }

    public string? LastReadMessageId { get; set; }
}
