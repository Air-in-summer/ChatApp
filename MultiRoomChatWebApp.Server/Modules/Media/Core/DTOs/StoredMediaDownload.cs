namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Kết quả đọc và truy xuất đối tượng (Object) từ hệ thống lưu trữ (Storage) để truyền tải về Client.
/// </summary>
public sealed class StoredMediaDownload
{
    /// <summary>Luồng dữ liệu (Stream) thực của tệp từ Storage (S3/MinIO/Local).</summary>
    public required Stream Content { get; init; }
    /// <summary>Kiểu dữ liệu MIME Type tương ứng với tệp.</summary>
    public required string ContentType { get; init; }
    /// <summary>Dung lượng của tệp tin.</summary>
    public long? SizeBytes { get; init; }
}