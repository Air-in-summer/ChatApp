using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Dữ liệu đầu vào để khởi tạo một siêu dữ liệu (Metadata) cho tệp phương tiện (Media) mới trong hệ thống.
/// </summary>
public sealed class CreateMediaAssetRequest
{
    /// <summary>Định danh người dùng sở hữu/tải lên tệp Media.</summary>
    public Guid OwnerUserId { get; init; }
    /// <summary>Phạm vi sử dụng của Media (ví dụ: Avatar, ChatAttachment, v.v.).</summary>
    public MediaScope Scope { get; init; }
    /// <summary>Phân loại tệp Media (Image, Video, Document...).</summary>
    public MediaKind Kind { get; init; }
    /// <summary>Mức độ truy cập (Public, Private, Restricted).</summary>
    public MediaAccessLevel AccessLevel { get; init; }
    /// <summary>Trạng thái hiện tại của Media (mặc định là Pending - đang chờ xử lý).</summary>
    public MediaAssetStatus Status { get; init; } = MediaAssetStatus.Pending;
    /// <summary>ID phòng chat nếu tệp Media được gắn vào một phòng cụ thể (Tùy chọn).</summary>
    public Guid? RoomId { get; init; }
    /// <summary>ID của tin nhắn trực tiếp chứa Media này (Tùy chọn).</summary>
    public string? MessageId { get; init; }
    /// <summary>Tên Bucket (trên Object Storage như S3) nơi lưu trữ file vật lý.</summary>
    public required string BucketName { get; init; }
    /// <summary>Đường dẫn khóa (Key) trỏ tới file vật lý trong Bucket.</summary>
    public required string StorageKey { get; init; }
    /// <summary>Tên gốc của file lúc người dùng tải lên.</summary>
    public required string OriginalFileName { get; init; }
    /// <summary>Định dạng MIME Type của file (ví dụ: image/jpeg).</summary>
    public required string ContentType { get; init; }
    /// <summary>Kích thước tệp tin (tính bằng byte).</summary>
    public long SizeBytes { get; init; }
    /// <summary>URL truy cập công khai của file (nếu Storage hỗ trợ Public Read).</summary>
    public string? PublicUrl { get; init; }
}