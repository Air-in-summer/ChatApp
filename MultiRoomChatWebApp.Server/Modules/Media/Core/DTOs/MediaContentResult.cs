namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Media da duoc check quyen va san sang stream ve client.
/// </summary>
public sealed class MediaContentResult
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public long? SizeBytes { get; init; }
}
