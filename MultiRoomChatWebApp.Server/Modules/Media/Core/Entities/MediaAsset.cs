using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;

public class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerUserId { get; set; }

    public MediaScope Scope { get; set; }

    public MediaKind Kind { get; set; }

    public MediaAccessLevel AccessLevel { get; set; }

    public MediaAssetStatus Status { get; set; } = MediaAssetStatus.Pending;

    public Guid? RoomId { get; set; }

    public string? MessageId { get; set; }

    public string? ReservedByMessageId { get; set; }

    public DateTime? ReservedAt { get; set; }

    public string BucketName { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? PublicUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? AttachedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User OwnerUser { get; set; } = null!;
}
