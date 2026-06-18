using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Xu ly upload icon Server: validate file, luu object public, cap nhat Group.IconUrl va cache metadata.
/// </summary>
public sealed class GroupIconMediaService : IGroupIconMediaService
{
    private readonly AppDbContext _dbContext;
    private readonly IGroupPermissionsCache _groupPermissionsCache;
    private readonly IGroupMetadataCache _groupMetadataCache;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly IMediaValidationService _mediaValidationService;
    private readonly MediaStorageOptions _options;
    private readonly ILogger<GroupIconMediaService> _logger;

    public GroupIconMediaService(
        AppDbContext dbContext,
        IGroupPermissionsCache groupPermissionsCache,
        IGroupMetadataCache groupMetadataCache,
        IMediaStorageService mediaStorageService,
        IMediaValidationService mediaValidationService,
        IOptions<MediaStorageOptions> options,
        ILogger<GroupIconMediaService> logger)
    {
        _dbContext = dbContext;
        _groupPermissionsCache = groupPermissionsCache;
        _groupMetadataCache = groupMetadataCache;
        _mediaStorageService = mediaStorageService;
        _mediaValidationService = mediaValidationService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GroupDto> UploadGroupIconAsync(
        Guid userId,
        Guid groupId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var role = await _groupPermissionsCache.GetMemberRoleAsync(groupId, userId);
        if (role != GroupRole.Owner)
        {
            throw ApiException.Forbidden(
                "group_icon_owner_required",
                "Chi chu so huu moi co quyen thay doi icon Server.");
        }

        var group = await _dbContext.Groups
            .FirstOrDefaultAsync(item => item.Id == groupId && item.DeletedAt == null, cancellationToken)
            ?? throw ApiException.NotFound("group_not_found", "Khong tim thay Server.");

        var validatedFile = await _mediaValidationService.ValidateAvatarAsync(file, cancellationToken);
        var mediaId = Guid.NewGuid();
        var storageKey = $"group-icons/{groupId}/{mediaId}{validatedFile.Extension}";
        StoredMediaObject? storedObject = null;
        var metadataSaved = false;

        try
        {
            await using var stream = file!.OpenReadStream();
            storedObject = await _mediaStorageService.PutObjectAsync(
                _options.PublicBucket,
                storageKey,
                stream,
                validatedFile.ContentType,
                validatedFile.SizeBytes,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(storedObject.PublicUrl))
            {
                throw new InvalidOperationException("Public group icon upload did not return a public URL.");
            }

            var updatedGroup = await SaveGroupIconAsync(
                group,
                userId,
                mediaId,
                validatedFile,
                storedObject,
                cancellationToken);
            metadataSaved = true;

            return updatedGroup;
        }
        catch
        {
            if (storedObject != null && !metadataSaved)
            {
                await TryDeleteUploadedObjectAsync(storedObject, cancellationToken);
            }

            throw;
        }
    }

    private async Task<GroupDto> SaveGroupIconAsync(
        MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.Group group,
        Guid userId,
        Guid mediaId,
        ValidatedMediaFile validatedFile,
        StoredMediaObject storedObject,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var groupIconPrefix = $"group-icons/{group.Id}/";
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var previousIcons = await _dbContext.MediaAssets
            .Where(asset => asset.Scope == MediaScope.GroupIcon &&
                            asset.StorageKey.StartsWith(groupIconPrefix) &&
                            asset.Status == MediaAssetStatus.Active &&
                            asset.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var asset in previousIcons)
        {
            asset.Status = MediaAssetStatus.Deleted;
            asset.DeletedAt = now;
        }

        _dbContext.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OwnerUserId = userId,
            Scope = MediaScope.GroupIcon,
            Kind = MediaKind.Image,
            AccessLevel = MediaAccessLevel.PublicRead,
            Status = MediaAssetStatus.Active,
            BucketName = storedObject.BucketName,
            StorageKey = storedObject.StorageKey,
            OriginalFileName = validatedFile.OriginalFileName,
            ContentType = validatedFile.ContentType,
            SizeBytes = validatedFile.SizeBytes,
            PublicUrl = storedObject.PublicUrl,
            CreatedAt = now
        });

        group.IconUrl = storedObject.PublicUrl;
        group.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var groupDto = new GroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            IconUrl = group.IconUrl,
            InviteCode = group.InviteCode,
            OwnerId = group.OwnerId,
            CreatedAt = group.CreatedAt
        };

        await _groupMetadataCache.SetGroupMetadataAsync(groupDto);
        return groupDto;
    }

    private async Task TryDeleteUploadedObjectAsync(
        StoredMediaObject storedObject,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediaStorageService.DeleteObjectAsync(
                storedObject.BucketName,
                storedObject.StorageKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Khong xoa duoc group icon object sau khi cap nhat DB that bai. Bucket={Bucket}; Key={Key}",
                storedObject.BucketName,
                storedObject.StorageKey);
        }
    }
}
