using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Xu ly avatar noi bo: validate file, luu object public, cap nhat metadata va profile user.
/// </summary>
public sealed class AvatarMediaService : IAvatarMediaService
{
    private readonly AppDbContext _dbContext;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly IMediaValidationService _mediaValidationService;
    private readonly IUserCacheService _userCacheService;
    private readonly MediaStorageOptions _options;
    private readonly ILogger<AvatarMediaService> _logger;

    public AvatarMediaService(
        AppDbContext dbContext,
        IMediaStorageService mediaStorageService,
        IMediaValidationService mediaValidationService,
        IUserCacheService userCacheService,
        IOptions<MediaStorageOptions> options,
        ILogger<AvatarMediaService> logger)
    {
        _dbContext = dbContext;
        _mediaStorageService = mediaStorageService;
        _mediaValidationService = mediaValidationService;
        _userCacheService = userCacheService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UserProfileDto> UploadAvatarAsync(
        Guid userId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var validatedFile = await _mediaValidationService.ValidateAvatarAsync(file, cancellationToken);
        var user = await GetActiveUserAsync(userId, cancellationToken);
        var mediaId = Guid.NewGuid();
        var storageKey = $"avatars/{userId}/{mediaId}{validatedFile.Extension}";
        StoredMediaObject? storedObject = null;
        var profileSaved = false;

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
                throw new InvalidOperationException("Public avatar upload did not return a public URL.");

            await SaveAvatarProfileAsync(user, mediaId, validatedFile, storedObject, cancellationToken);
            profileSaved = true;
            await _userCacheService.InvalidateUserAsync(user.Id);

            return MapToProfileDto(user);
        }
        catch
        {
            if (storedObject != null && !profileSaved)
            {
                await TryDeleteUploadedObjectAsync(storedObject, cancellationToken);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<UserProfileDto> DeleteAvatarAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken);
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var activeAvatarAssets = await _dbContext.MediaAssets
            .Where(asset => asset.OwnerUserId == user.Id &&
                            asset.Scope == MediaScope.Avatar &&
                            asset.Status == MediaAssetStatus.Active &&
                            asset.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var asset in activeAvatarAssets)
        {
            asset.Status = MediaAssetStatus.Deleted;
            asset.DeletedAt = now;
        }

        user.AvatarUrl = null;
        user.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _userCacheService.InvalidateUserAsync(user.Id);

        return MapToProfileDto(user);
    }

    private async Task SaveAvatarProfileAsync(
        MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User user,
        Guid mediaId,
        ValidatedMediaFile validatedFile,
        StoredMediaObject storedObject,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var previousAvatarAssets = await _dbContext.MediaAssets
            .Where(asset => asset.OwnerUserId == user.Id &&
                            asset.Scope == MediaScope.Avatar &&
                            asset.Status == MediaAssetStatus.Active &&
                            asset.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var asset in previousAvatarAssets)
        {
            asset.Status = MediaAssetStatus.Deleted;
            asset.DeletedAt = now;
        }

        _dbContext.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OwnerUserId = user.Id,
            Scope = MediaScope.Avatar,
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

        user.AvatarUrl = storedObject.PublicUrl;
        user.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User> GetActiveUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);

        return user ?? throw ApiException.NotFound("user_not_found", "Không tìm thấy tài khoản.");
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
                "Khong xoa duoc avatar object sau khi cap nhat DB that bai. Bucket={Bucket}; Key={Key}",
                storedObject.BucketName,
                storedObject.StorageKey);
        }
    }

    private static UserProfileDto MapToProfileDto(MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl
        };
    }
}
