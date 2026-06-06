using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Xu ly chat media upload dang pending va cap signed URL cho client.
/// </summary>
public sealed class ChatMediaService : IChatMediaService
{
    private readonly AppDbContext _dbContext;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly IMediaValidationService _mediaValidationService;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IGroupPermissionsCache _groupPermissionsCache;
    private readonly MediaStorageOptions _options;
    private readonly ILogger<ChatMediaService> _logger;

    public ChatMediaService(
        AppDbContext dbContext,
        IMediaStorageService mediaStorageService,
        IMediaValidationService mediaValidationService,
        IRoomMetadataCache roomMetadataCache,
        IRoomPermissionsCache roomPermissionsCache,
        IGroupPermissionsCache groupPermissionsCache,
        IOptions<MediaStorageOptions> options,
        ILogger<ChatMediaService> logger)
    {
        _dbContext = dbContext;
        _mediaStorageService = mediaStorageService;
        _mediaValidationService = mediaValidationService;
        _roomMetadataCache = roomMetadataCache;
        _roomPermissionsCache = roomPermissionsCache;
        _groupPermissionsCache = groupPermissionsCache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MediaUploadResultDto> UploadPendingAsync(
        Guid ownerUserId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        await EnsureActiveUserAsync(ownerUserId, cancellationToken);

        var validatedFile = await _mediaValidationService.ValidateChatMediaAsync(file, cancellationToken);
        var mediaId = Guid.NewGuid();
        var storageKey = $"{validatedFile.StoragePrefix}/{ownerUserId}/{mediaId}{validatedFile.Extension}";
        StoredMediaObject? storedObject = null;
        var metadataSaved = false;

        try
        {
            await using var stream = file!.OpenReadStream();
            storedObject = await _mediaStorageService.PutObjectAsync(
                _options.PrivateBucket,
                storageKey,
                stream,
                validatedFile.ContentType,
                validatedFile.SizeBytes,
                cancellationToken);

            var now = DateTime.UtcNow;
            _dbContext.MediaAssets.Add(new MediaAsset
            {
                Id = mediaId,
                OwnerUserId = ownerUserId,
                Scope = MediaScope.ChatAttachment,
                Kind = validatedFile.Kind,
                AccessLevel = MediaAccessLevel.PrivateSignedUrl,
                Status = MediaAssetStatus.Pending,
                RoomId = null,
                BucketName = storedObject.BucketName,
                StorageKey = storedObject.StorageKey,
                OriginalFileName = validatedFile.OriginalFileName,
                ContentType = validatedFile.ContentType,
                SizeBytes = validatedFile.SizeBytes,
                CreatedAt = now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            metadataSaved = true;

            var ttl = GetSignedUrlTtl();
            return new MediaUploadResultDto
            {
                MediaId = mediaId,
                Kind = validatedFile.Kind,
                Filename = validatedFile.OriginalFileName,
                Size = validatedFile.SizeBytes,
                MimeType = validatedFile.ContentType,
                PreviewUrl = _mediaStorageService.CreatePresignedGetUrl(
                    storedObject.BucketName,
                    storedObject.StorageKey,
                    ttl),
                ExpiresAt = DateTime.UtcNow.Add(ttl)
            };
        }
        catch
        {
            if (storedObject != null && !metadataSaved)
            {
                await TryDeleteObjectAsync(storedObject.BucketName, storedObject.StorageKey, cancellationToken);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MediaAccessUrlDto> CreateAccessUrlAsync(
        Guid currentUserId,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var mediaAsset = await _dbContext.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(asset => asset.Id == mediaId, cancellationToken);

        if (mediaAsset == null || mediaAsset.Status == MediaAssetStatus.Deleted || mediaAsset.DeletedAt != null)
            throw ApiException.NotFound("media_not_found", "Không tìm thấy media.");

        if (mediaAsset.AccessLevel == MediaAccessLevel.PublicRead)
        {
            if (string.IsNullOrWhiteSpace(mediaAsset.PublicUrl))
                throw ApiException.Conflict("media_public_url_missing", "Media public chưa có URL hợp lệ.");

            return new MediaAccessUrlDto
            {
                MediaId = mediaAsset.Id,
                Url = mediaAsset.PublicUrl,
                ExpiresAt = null
            };
        }

        await EnsureCanAccessPrivateMediaAsync(mediaAsset, currentUserId);

        var ttl = GetSignedUrlTtl();
        return new MediaAccessUrlDto
        {
            MediaId = mediaAsset.Id,
            Url = _mediaStorageService.CreatePresignedGetUrl(
                mediaAsset.BucketName,
                mediaAsset.StorageKey,
                ttl),
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }

    /// <inheritdoc />
    public async Task<MediaContentResult> OpenContentAsync(
        Guid currentUserId,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var mediaAsset = await _dbContext.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(asset => asset.Id == mediaId, cancellationToken);

        if (mediaAsset == null || mediaAsset.Status == MediaAssetStatus.Deleted || mediaAsset.DeletedAt != null)
            throw ApiException.NotFound("media_not_found", "Khong tim thay media.");

        if (mediaAsset.AccessLevel != MediaAccessLevel.PublicRead)
        {
            await EnsureCanAccessPrivateMediaAsync(mediaAsset, currentUserId);
        }

        var storedObject = await _mediaStorageService.GetObjectAsync(
            mediaAsset.BucketName,
            mediaAsset.StorageKey,
            cancellationToken);

        return new MediaContentResult
        {
            Content = storedObject.Content,
            ContentType = string.IsNullOrWhiteSpace(mediaAsset.ContentType)
                ? storedObject.ContentType
                : mediaAsset.ContentType,
            SizeBytes = mediaAsset.SizeBytes > 0 ? mediaAsset.SizeBytes : storedObject.SizeBytes
        };
    }

    /// <inheritdoc />
    public async Task CancelPendingAsync(
        Guid ownerUserId,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var mediaAsset = await _dbContext.MediaAssets
            .FirstOrDefaultAsync(asset => asset.Id == mediaId, cancellationToken);

        if (mediaAsset == null || mediaAsset.DeletedAt != null)
            throw ApiException.NotFound("media_not_found", "Không tìm thấy media.");

        if (mediaAsset.OwnerUserId != ownerUserId)
            throw ApiException.Forbidden("media_not_owned", "Bạn không có quyền thao tác media này.");

        if (mediaAsset.Scope != MediaScope.ChatAttachment ||
            mediaAsset.Status != MediaAssetStatus.Pending ||
            mediaAsset.RoomId != null)
        {
            throw ApiException.Conflict("media_not_pending", "Media này không còn ở trạng thái chờ gửi.");
        }

        await _mediaStorageService.DeleteObjectAsync(
            mediaAsset.BucketName,
            mediaAsset.StorageKey,
            cancellationToken);

        mediaAsset.Status = MediaAssetStatus.Deleted;
        mediaAsset.DeletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCanAccessPrivateMediaAsync(MediaAsset mediaAsset, Guid currentUserId)
    {
        if (mediaAsset.Status == MediaAssetStatus.Pending && mediaAsset.RoomId == null)
        {
            if (mediaAsset.OwnerUserId != currentUserId)
                throw ApiException.Forbidden("media_not_owned", "Bạn không có quyền xem media này.");

            return;
        }

        if (!mediaAsset.RoomId.HasValue)
            throw ApiException.Conflict("media_room_missing", "Media chưa được gắn với phòng hợp lệ.");

        if (!await CanAccessRoomAsync(mediaAsset.RoomId.Value, currentUserId))
            throw ApiException.Forbidden("media_room_forbidden", "Bạn không có quyền xem media này.");
    }

    private async Task<bool> CanAccessRoomAsync(Guid roomId, Guid userId)
    {
        var roomMetadata = await _roomMetadataCache.GetRoomMetadataAsync(roomId);
        if (roomMetadata == null)
            throw ApiException.NotFound("room_not_found", "Không tìm thấy phòng.");

        if (roomMetadata.Value.GroupId.HasValue && !roomMetadata.Value.IsPrivate)
            return await _groupPermissionsCache.IsUserInGroupAsync(roomMetadata.Value.GroupId.Value, userId);

        return await _roomPermissionsCache.IsUserInRoomAsync(roomId, userId);
    }

    private async Task EnsureActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken);

        if (!exists)
            throw ApiException.NotFound("user_not_found", "Không tìm thấy tài khoản.");
    }

    private TimeSpan GetSignedUrlTtl()
    {
        return TimeSpan.FromMinutes(Math.Max(1, _options.SignedUrlMinutes));
    }

    private async Task TryDeleteObjectAsync(
        string bucketName,
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediaStorageService.DeleteObjectAsync(bucketName, storageKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Khong xoa duoc chat media object sau khi ghi metadata that bai. Bucket={Bucket}; Key={Key}",
                bucketName,
                storageKey);
        }
    }
}
