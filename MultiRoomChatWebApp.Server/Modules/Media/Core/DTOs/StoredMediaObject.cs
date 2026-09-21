namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Kết quả trả về từ phía Storage Service (S3/MinIO) sau khi ghi vật lý một đối tượng Media thành công.
/// </summary>
public sealed class StoredMediaObject
{
    /// <summary>Tên Bucket chứa đối tượng vừa ghi.</summary>
    public required string BucketName { get; init; }
    /// <summary>Đường dẫn Khóa (Key) định danh file bên trong Bucket.</summary>
    public required string StorageKey { get; init; }
    /// <summary>Định dạng MIME Type của file trên Storage.</summary>
    public required string ContentType { get; init; }
    /// <summary>Dung lượng tệp (Bytes) mà Storage báo lại.</summary>
    public long? SizeBytes { get; init; }
    /// <summary>URL truy cập công khai (nếu có cấu hình Public cho Bucket này).</summary>
    public string? PublicUrl { get; init; }
}