using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <inheritdoc />
public sealed class MediaService : IMediaService
{
    private readonly AppDbContext _dbContext;

    public MediaService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<MediaAssetDto> CreateAsync(
        CreateMediaAssetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StorageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OriginalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);

        if (request.SizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(request.SizeBytes), "Media size cannot be negative.");

        var mediaAsset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            OwnerUserId = request.OwnerUserId,
            Scope = request.Scope,
            Kind = request.Kind,
            AccessLevel = request.AccessLevel,
            Status = request.Status,
            RoomId = request.RoomId,
            MessageId = request.MessageId,
            BucketName = request.BucketName,
            StorageKey = request.StorageKey,
            OriginalFileName = request.OriginalFileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            PublicUrl = request.PublicUrl,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.MediaAssets.Add(mediaAsset);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(mediaAsset);
    }

    /// <inheritdoc />
    public async Task<MediaAssetDto?> GetByIdAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        var mediaAsset = await _dbContext.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(asset => asset.Id == mediaId, cancellationToken);

        return mediaAsset == null ? null : ToDto(mediaAsset);
    }

    /// <inheritdoc />
    public async Task<MediaAssetDto?> MarkAttachedAsync(
        Guid mediaId,
        string messageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var mediaAsset = await _dbContext.MediaAssets
            .FirstOrDefaultAsync(asset => asset.Id == mediaId, cancellationToken);

        if (mediaAsset == null)
            return null;

        if (mediaAsset.Status == MediaAssetStatus.Deleted)
            throw new InvalidOperationException("Deleted media asset cannot be attached.");

        if (mediaAsset.Status == MediaAssetStatus.Reserved &&
            mediaAsset.ReservedByMessageId != messageId)
        {
            throw new InvalidOperationException("Media asset is reserved by another message.");
        }

        if (mediaAsset.Status == MediaAssetStatus.Attached &&
            mediaAsset.MessageId != messageId)
        {
            throw new InvalidOperationException("Media asset is already attached to another message.");
        }

        mediaAsset.Status = MediaAssetStatus.Attached;
        mediaAsset.MessageId = messageId;
        mediaAsset.AttachedAt ??= DateTime.UtcNow;
        mediaAsset.ReservedByMessageId = null;
        mediaAsset.ReservedAt = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(mediaAsset);
    }

    /// <inheritdoc />
    public async Task<MediaAssetDto?> SoftDeleteAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        var mediaAsset = await _dbContext.MediaAssets
            .FirstOrDefaultAsync(asset => asset.Id == mediaId, cancellationToken);

        if (mediaAsset == null)
            return null;

        mediaAsset.Status = MediaAssetStatus.Deleted;
        mediaAsset.DeletedAt ??= DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(mediaAsset);
    }

    private static MediaAssetDto ToDto(MediaAsset mediaAsset)
    {
        return new MediaAssetDto
        {
            Id = mediaAsset.Id,
            OwnerUserId = mediaAsset.OwnerUserId,
            Scope = mediaAsset.Scope,
            Kind = mediaAsset.Kind,
            AccessLevel = mediaAsset.AccessLevel,
            Status = mediaAsset.Status,
            RoomId = mediaAsset.RoomId,
            MessageId = mediaAsset.MessageId,
            ReservedByMessageId = mediaAsset.ReservedByMessageId,
            ReservedAt = mediaAsset.ReservedAt,
            BucketName = mediaAsset.BucketName,
            StorageKey = mediaAsset.StorageKey,
            OriginalFileName = mediaAsset.OriginalFileName,
            ContentType = mediaAsset.ContentType,
            SizeBytes = mediaAsset.SizeBytes,
            PublicUrl = mediaAsset.PublicUrl,
            CreatedAt = mediaAsset.CreatedAt,
            AttachedAt = mediaAsset.AttachedAt,
            DeletedAt = mediaAsset.DeletedAt
        };
    }
}
