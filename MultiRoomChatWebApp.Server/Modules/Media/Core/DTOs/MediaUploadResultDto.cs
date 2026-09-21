using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Kết quả trả về cho Client sau khi quá trình Upload tệp Media hoàn thành.
/// </summary>
public sealed class MediaUploadResultDto
{
    /// <summary>Định danh duy nhất của Media vừa tải lên.</summary>
    public Guid MediaId { get; init; }
    /// <summary>Loại tệp đã tải (Image, Video...).</summary>
    public MediaKind Kind { get; init; }
    /// <summary>Tên tệp đã được xử lý hoặc lưu trên hệ thống.</summary>
    public string Filename { get; init; } = string.Empty;
    /// <summary>Kích thước tệp tải lên (Bytes).</summary>
    public long Size { get; init; }
    /// <summary>Định dạng MIME Type của tệp tải lên.</summary>
    public string MimeType { get; init; } = string.Empty;
    /// <summary>URL dùng để xem trước (Thumbnail/Preview) nếu có.</summary>
    public string PreviewUrl { get; init; } = string.Empty;
    /// <summary>Thời gian URL Preview hết hạn (nếu sử dụng cơ chế bảo mật chia sẻ).</summary>
    public DateTime ExpiresAt { get; init; }
}