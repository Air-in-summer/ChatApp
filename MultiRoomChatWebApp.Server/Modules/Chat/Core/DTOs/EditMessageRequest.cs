namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Du lieu cap nhat noi dung tin nhan.
/// </summary>
public sealed class EditMessageRequest
{
    public string Content { get; set; } = string.Empty;
}
