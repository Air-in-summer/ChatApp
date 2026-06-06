using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Ket qua validate file media truoc khi ghi vao object storage.
/// </summary>
public sealed class ValidatedMediaFile
{
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public required string Extension { get; init; }
    public long SizeBytes { get; init; }
    public MediaKind Kind { get; init; } = MediaKind.File;
    public string StoragePrefix { get; init; } = string.Empty;
}
