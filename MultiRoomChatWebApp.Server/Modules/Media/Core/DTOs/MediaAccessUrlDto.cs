namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

/// <summary>
/// Thông tin đường dẫn để truy cập một tệp phương tiện (Media).
/// </summary>
public sealed class MediaAccessUrlDto
{
    /// <summary>Định danh duy nhất của Media.</summary>
    public Guid MediaId { get; init; }
    /// <summary>Đường dẫn (URL) để tải hoặc xem Media. Thường là Pre-signed URL.</summary>
    public string Url { get; init; } = string.Empty;
    /// <summary>Thời điểm URL sẽ hết hạn truy cập (nếu là Pre-signed URL).</summary>
    public DateTime? ExpiresAt { get; init; }
}