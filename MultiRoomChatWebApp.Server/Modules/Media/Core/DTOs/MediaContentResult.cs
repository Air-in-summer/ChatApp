namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Dữ liệu đầu ra chứa luồng stream vật lý của một Media.
/// Thường dùng sau khi đã kiểm tra quyền hạn (Authorization) thành công để trả stream về cho Client qua API backend.
/// </summary>
public sealed class MediaContentResult
{
    /// <summary>Luồng dữ liệu (Stream) của tệp vật lý.</summary>
    public required Stream Content { get; init; }
    /// <summary>Định dạng MIME Type để Backend trả về HTTP Header chính xác.</summary>
    public required string ContentType { get; init; }
    /// <summary>Kích thước tệp để hỗ trợ Content-Length hoặc Partial Content (Stream tải từng phần).</summary>
    public long? SizeBytes { get; init; }
}