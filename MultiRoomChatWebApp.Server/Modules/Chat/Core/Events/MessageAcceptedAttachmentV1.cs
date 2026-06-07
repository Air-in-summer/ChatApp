using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

public sealed class MessageAcceptedAttachmentV1
{
    public Guid MediaId { get; init; }

    public MediaKind Kind { get; init; } = MediaKind.File;

    public MediaAccessLevel AccessLevel { get; init; } = MediaAccessLevel.PrivateSignedUrl;

    public string BucketName { get; init; } = string.Empty;

    public string StorageKey { get; init; } = string.Empty;

    public string Filename { get; init; } = string.Empty;

    public long Size { get; init; }

    public string MimeType { get; init; } = string.Empty;

    public string? PublicUrl { get; init; }
}
