using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

public sealed class MediaAssetDto
{
    public Guid Id { get; init; }
    public Guid OwnerUserId { get; init; }
    public MediaScope Scope { get; init; }
    public MediaKind Kind { get; init; }
    public MediaAccessLevel AccessLevel { get; init; }
    public MediaAssetStatus Status { get; init; }
    public Guid? RoomId { get; init; }
    public string? MessageId { get; init; }
    public string? ReservedByMessageId { get; init; }
    public DateTime? ReservedAt { get; init; }
    public string BucketName { get; init; } = string.Empty;
    public string StorageKey { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? PublicUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? AttachedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
}
