namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Ket qua doc object media tu storage de stream ve client qua backend.
/// </summary>
public sealed class StoredMediaDownload
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public long? SizeBytes { get; init; }
}
