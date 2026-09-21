using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

/// <summary>
/// Giao diện quản lý vòng đời và thông tin siêu dữ liệu (Metadata) của các tệp phương tiện (Media).
/// </summary>
public interface IMediaService
{
    /// <summary>
    /// Khởi tạo và lưu trữ thông tin về một tệp phương tiện mới vào hệ thống.
    /// </summary>
    Task<MediaAssetDto> CreateAsync(CreateMediaAssetRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Lấy thông tin chi tiết của một tệp phương tiện dựa trên ID.
    /// </summary>
    Task<MediaAssetDto?> GetByIdAsync(Guid mediaId, CancellationToken cancellationToken);

    /// <summary>
    /// Đánh dấu một tệp phương tiện đã được đính kèm thành công vào một tin nhắn.
    /// </summary>
    Task<MediaAssetDto?> MarkAttachedAsync(
        Guid mediaId,
        string messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Xóa mềm (Soft delete) một tệp phương tiện khỏi hệ thống.
    /// </summary>
    Task<MediaAssetDto?> SoftDeleteAsync(Guid mediaId, CancellationToken cancellationToken);
}
