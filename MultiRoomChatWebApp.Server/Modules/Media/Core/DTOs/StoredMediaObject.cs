namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Ket qua sau khi ghi mot object vao media storage.
/// </summary>
public sealed class StoredMediaObject
{
    public required string BucketName { get; init; }
    public required string StorageKey { get; init; }
    public required string ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public string? PublicUrl { get; init; }
}
