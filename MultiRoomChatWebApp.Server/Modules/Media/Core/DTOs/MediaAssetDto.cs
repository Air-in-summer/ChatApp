using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Đối tượng truyền tải dữ liệu chi tiết (DTO) của một tệp phương tiện (Media Asset).
/// </summary>
public sealed class MediaAssetDto
{
    /// <summary>Định danh duy nhất của Media trong hệ thống.</summary>
    public Guid Id { get; init; }
    /// <summary>Định danh người tải lên (Owner).</summary>
    public Guid OwnerUserId { get; init; }
    /// <summary>Phạm vi sử dụng của Media (Avatar, ChatAttachment, v.v.).</summary>
    public MediaScope Scope { get; init; }
    /// <summary>Loại tệp phương tiện (Image, Video, Document, v.v.).</summary>
    public MediaKind Kind { get; init; }
    /// <summary>Mức độ bảo vệ truy cập (Public, Private, Restricted).</summary>
    public MediaAccessLevel AccessLevel { get; init; }
    /// <summary>Trạng thái vòng đời của Media (Pending, Uploaded, Attached, Deleted).</summary>
    public MediaAssetStatus Status { get; init; }
    /// <summary>ID phòng chat mà Media thuộc về (Tùy chọn).</summary>
    public Guid? RoomId { get; init; }
    /// <summary>ID tin nhắn đính kèm Media này (Tùy chọn).</summary>
    public string? MessageId { get; init; }
    /// <summary>ID của tin nhắn đang được cấp quyền tạm thời (giữ chỗ) chờ gửi.</summary>
    public string? ReservedByMessageId { get; init; }
    /// <summary>Thời gian thao tác giữ chỗ (Reservation) được thực hiện.</summary>
    public DateTime? ReservedAt { get; init; }
    /// <summary>Tên Bucket (S3/MinIO) đang lưu trữ tệp.</summary>
    public string BucketName { get; init; } = string.Empty;
    /// <summary>Đường dẫn (Key) vật lý trong Bucket.</summary>
    public string StorageKey { get; init; } = string.Empty;
    /// <summary>Tên file nguyên gốc lúc tải lên.</summary>
    public string OriginalFileName { get; init; } = string.Empty;
    /// <summary>Định dạng dữ liệu MIME Type (ví dụ: image/png).</summary>
    public string ContentType { get; init; } = string.Empty;
    /// <summary>Kích thước tệp (bytes).</summary>
    public long SizeBytes { get; init; }
    /// <summary>Đường link công khai trực tiếp (nếu có).</summary>
    public string? PublicUrl { get; init; }
    /// <summary>Thời gian bản ghi Metadata được tạo ra.</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>Thời gian tệp Media được chính thức đính kèm vào tin nhắn (hoặc một domain khác).</summary>
    public DateTime? AttachedAt { get; init; }
    /// <summary>Thời gian tệp bị xóa mềm (Soft delete).</summary>
    public DateTime? DeletedAt { get; init; }
}