using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

public sealed class MediaUploadResultDto
{
    public Guid MediaId { get; init; }
    public MediaKind Kind { get; init; }
    public string Filename { get; init; } = string.Empty;
    public long Size { get; init; }
    public string MimeType { get; init; } = string.Empty;
    public string PreviewUrl { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}
