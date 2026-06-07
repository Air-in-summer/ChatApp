namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Du lieu them hoac go reaction khoi tin nhan.
/// </summary>
public sealed class MessageReactionRequest
{
    public string Emoji { get; set; } = string.Empty;
}
