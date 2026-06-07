namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed class SendMessageRequest
{
    public Guid RoomId { get; set; }

    public string Content { get; set; } = string.Empty;

    public Guid ClientMessageId { get; set; }

    public List<Guid> MediaIds { get; set; } = [];
}
