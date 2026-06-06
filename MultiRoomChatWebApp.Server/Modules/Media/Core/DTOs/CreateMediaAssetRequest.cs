using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

public sealed class CreateMediaAssetRequest
{
    public Guid OwnerUserId { get; init; }
    public MediaScope Scope { get; init; }
    public MediaKind Kind { get; init; }
    public MediaAccessLevel AccessLevel { get; init; }
    public MediaAssetStatus Status { get; init; } = MediaAssetStatus.Pending;
    public Guid? RoomId { get; init; }
    public string? MessageId { get; init; }
    public required string BucketName { get; init; }
    public required string StorageKey { get; init; }
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public string? PublicUrl { get; init; }
}
