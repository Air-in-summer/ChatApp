using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

/// <summary>
/// Hop dong luu tru object media tren S3-compatible storage.
/// </summary>
public interface IMediaStorageService
{
    /// <summary>
    /// Dam bao cac bucket media bat buoc ton tai va policy public/private dung cho local/dev.
    /// </summary>
    Task EnsureBucketsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ghi mot object vao bucket chi dinh.
    /// </summary>
    Task<StoredMediaObject> PutObjectAsync(
        string bucketName,
        string storageKey,
        Stream content,
        string contentType,
        long? sizeBytes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Xoa mot object khoi bucket chi dinh.
    /// </summary>
    Task DeleteObjectAsync(string bucketName, string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// Doc object tu storage de backend co the stream ve client qua HTTPS cung origin API.
    /// </summary>
    Task<StoredMediaDownload> GetObjectAsync(
        string bucketName,
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tao URL doc tam thoi cho object private sau khi caller da check quyen.
    /// </summary>
    string CreatePresignedGetUrl(string bucketName, string storageKey, TimeSpan? ttl = null);
}
