using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <inheritdoc />
public sealed class MessageDeliveryAttachmentResolver : IMessageDeliveryAttachmentResolver
{
    private readonly AppDbContext _dbContext;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly MediaStorageOptions _mediaOptions;

    public MessageDeliveryAttachmentResolver(
        AppDbContext dbContext,
        IMediaStorageService mediaStorageService,
        IOptions<MediaStorageOptions> mediaOptions)
    {
        _dbContext = dbContext;
        _mediaStorageService = mediaStorageService;
        _mediaOptions = mediaOptions.Value;
    }

    /// <inheritdoc />
    public async Task<MessageDeliveryAttachmentResult> ResolveAsync(
        MessageAcceptedEventV1 acceptedEvent,
        DateTime deliveryAtUtc,
        CancellationToken cancellationToken)
    {
        var mediaIds = acceptedEvent.MediaIds ?? [];
        var snapshots = acceptedEvent.Attachments ?? [];

        if (mediaIds.Count == 0)
        {
            return snapshots.Count == 0
                ? MessageDeliveryAttachmentResult.Resolved([])
                : MessageDeliveryAttachmentResult.Failed(
                    "delivery_attachment_count_mismatch",
                    "Event khong co MediaIds nhung van co attachment snapshot.");
        }

        if (mediaIds.Count != snapshots.Count ||
            mediaIds.Any(mediaId => mediaId == Guid.Empty) ||
            mediaIds.Distinct().Count() != mediaIds.Count)
        {
            return MessageDeliveryAttachmentResult.Failed(
                "delivery_attachment_ids_invalid",
                "MediaIds va attachment snapshot khong nhat quan.");
        }

        var assets = await _dbContext.MediaAssets
            .AsNoTracking()
            .Where(asset => mediaIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);

        if (assets.Count != mediaIds.Count)
        {
            return MessageDeliveryAttachmentResult.Failed(
                "delivery_attachment_missing",
                "Khong tim thay day du MediaAsset cua tin nhan.");
        }

        var assetsById = assets.ToDictionary(asset => asset.Id);
        var deliveryAttachments = new List<Attachment>(mediaIds.Count);
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _mediaOptions.SignedUrlMinutes));

        for (var index = 0; index < mediaIds.Count; index++)
        {
            var mediaId = mediaIds[index];
            var snapshot = snapshots[index];
            var asset = assetsById[mediaId];

            var stateError = ValidateAssetState(asset, acceptedEvent);
            if (stateError is not null)
            {
                return MessageDeliveryAttachmentResult.Failed(
                    stateError.Value.Code,
                    stateError.Value.Reason);
            }

            if (!MatchesImmutableSnapshot(asset, snapshot))
            {
                return MessageDeliveryAttachmentResult.Failed(
                    "delivery_attachment_snapshot_conflict",
                    $"Metadata cua attachment {mediaId} khong khop event bat bien.");
            }

            var isPublic = asset.AccessLevel == MediaAccessLevel.PublicRead;
            if (isPublic && string.IsNullOrWhiteSpace(asset.PublicUrl))
            {
                return MessageDeliveryAttachmentResult.Failed(
                    "delivery_attachment_public_url_missing",
                    $"Attachment public {mediaId} khong co public URL.");
            }

            var url = isPublic
                ? asset.PublicUrl!
                : _mediaStorageService.CreatePresignedGetUrl(
                    asset.BucketName,
                    asset.StorageKey,
                    ttl);

            deliveryAttachments.Add(new Attachment
            {
                MediaId = asset.Id,
                Kind = asset.Kind,
                Filename = asset.OriginalFileName,
                Size = asset.SizeBytes,
                MimeType = asset.ContentType,
                Url = url,
                ExpiresAt = isPublic ? null : deliveryAtUtc.Add(ttl)
            });
        }

        return MessageDeliveryAttachmentResult.Resolved(deliveryAttachments);
    }

    private static (string Code, string Reason)? ValidateAssetState(
        MediaAsset asset,
        MessageAcceptedEventV1 acceptedEvent)
    {
        if (asset.OwnerUserId != acceptedEvent.SenderId ||
            asset.Scope != MediaScope.ChatAttachment)
        {
            return (
                "delivery_attachment_owner_or_scope_invalid",
                $"Attachment {asset.Id} sai owner hoac khong phai chat attachment.");
        }

        if (asset.DeletedAt.HasValue || asset.Status == MediaAssetStatus.Deleted)
        {
            return (
                "delivery_attachment_deleted",
                $"Attachment {asset.Id} da bi xoa.");
        }

        var reservedByMessage =
            asset.Status == MediaAssetStatus.Reserved &&
            asset.RoomId is null &&
            asset.MessageId is null &&
            asset.ReservedByMessageId == acceptedEvent.MessageId &&
            asset.ReservedAt.HasValue;

        var attachedToMessage =
            asset.Status == MediaAssetStatus.Attached &&
            asset.RoomId == acceptedEvent.RoomId &&
            asset.MessageId == acceptedEvent.MessageId &&
            asset.ReservedByMessageId is null &&
            asset.ReservedAt is null;

        if (reservedByMessage || attachedToMessage)
        {
            return null;
        }

        return asset.Status switch
        {
            MediaAssetStatus.Pending => (
                "delivery_attachment_pending",
                $"Attachment {asset.Id} dang Pending, khong thuoc message."),
            MediaAssetStatus.CleanupInProgress => (
                "delivery_attachment_cleanup_in_progress",
                $"Attachment {asset.Id} dang duoc cleanup."),
            MediaAssetStatus.Reserved => (
                "delivery_attachment_reserved_by_other_message",
                $"Attachment {asset.Id} duoc reservation boi message khac."),
            MediaAssetStatus.Attached => (
                "delivery_attachment_attached_to_other_message",
                $"Attachment {asset.Id} da gan vao message khac."),
            _ => (
                "delivery_attachment_state_invalid",
                $"Attachment {asset.Id} co trang thai khong hop le cho delivery: {asset.Status}.")
        };
    }

    private static bool MatchesImmutableSnapshot(
        MediaAsset asset,
        MessageAcceptedAttachmentV1 snapshot)
    {
        var publicUrlMatches = asset.AccessLevel == MediaAccessLevel.PublicRead
            ? !string.IsNullOrWhiteSpace(snapshot.PublicUrl) &&
              string.Equals(asset.PublicUrl, snapshot.PublicUrl, StringComparison.Ordinal)
            : string.IsNullOrWhiteSpace(snapshot.PublicUrl);

        return snapshot.MediaId == asset.Id &&
               snapshot.Kind == asset.Kind &&
               snapshot.AccessLevel == asset.AccessLevel &&
               string.Equals(snapshot.BucketName, asset.BucketName, StringComparison.Ordinal) &&
               string.Equals(snapshot.StorageKey, asset.StorageKey, StringComparison.Ordinal) &&
               string.Equals(snapshot.Filename, asset.OriginalFileName, StringComparison.Ordinal) &&
               snapshot.Size == asset.SizeBytes &&
               string.Equals(snapshot.MimeType, asset.ContentType, StringComparison.Ordinal) &&
               publicUrlMatches;
    }
}
