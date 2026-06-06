namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

public sealed class MediaAccessUrlDto
{
    public Guid MediaId { get; init; }
    public string Url { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
}
