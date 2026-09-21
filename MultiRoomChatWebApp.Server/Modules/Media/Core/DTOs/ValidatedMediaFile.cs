using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Dữ liệu đầu ra sau quá trình kiểm tra (Validate) tính toàn vẹn và độ an toàn của một file Media, 
/// sẵn sàng để được hệ thống đẩy vào Object Storage.
/// </summary>
public sealed class ValidatedMediaFile
{
    /// <summary>Tên file nguyên gốc (đã được làm sạch, chống Path Traversal).</summary>
    public required string OriginalFileName { get; init; }
    /// <summary>Định dạng thực sự của file sau khi kiểm tra bằng Magic Bytes (MIME Type).</summary>
    public required string ContentType { get; init; }
    /// <summary>Đuôi mở rộng của file an toàn.</summary>
    public required string Extension { get; init; }
    /// <summary>Dung lượng thực tế của file.</summary>
    public long SizeBytes { get; init; }
    /// <summary>Phân loại Media (Ảnh, Video hay Tệp thường).</summary>
    public MediaKind Kind { get; init; } = MediaKind.File;
    /// <summary>Đường dẫn tiền tố (Prefix / Thư mục con) được định hướng lưu trữ trên Bucket (vd: images/2026/05).</summary>
    public string StoragePrefix { get; init; } = string.Empty;
}