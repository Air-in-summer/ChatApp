namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed class SendMessageRequest
{
    public Guid RoomId { get; set; }

    public string Content { get; set; } = string.Empty;

    public string TempId { get; set; } = string.Empty;

    public List<Guid> MediaIds { get; set; } = [];
}
