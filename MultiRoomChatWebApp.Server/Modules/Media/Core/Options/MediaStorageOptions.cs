namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Options;

/// <summary>
/// Cau hinh object storage dung cho avatar va chat media.
/// </summary>
public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    public string Provider { get; set; } = "S3";
    public string Endpoint { get; set; } = string.Empty;
    public string PublicEndpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string PublicBucket { get; set; } = "chatapp-public-media";
    public string PrivateBucket { get; set; } = "chatapp-private-media";
    public bool ForcePathStyle { get; set; } = true;
    public int SignedUrlMinutes { get; set; } = 15;
    public string LocalDataPath { get; set; } = @"D:\ChatAppData\minio";
    public long MaxAvatarBytes { get; set; } = 2 * 1024 * 1024;
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxAudioBytes { get; set; } = 25 * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 100 * 1024 * 1024;
    public long MaxFileBytes { get; set; } = 25 * 1024 * 1024;
    public int PendingCleanupIntervalMinutes { get; set; } = 30;
    public int PendingMediaTtlHours { get; set; } = 24;
    public int PendingCleanupBatchSize { get; set; } = 100;
}
