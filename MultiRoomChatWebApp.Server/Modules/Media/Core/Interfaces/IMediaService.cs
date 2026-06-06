using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

public interface IMediaService
{
    Task<MediaAssetDto> CreateAsync(CreateMediaAssetRequest request, CancellationToken cancellationToken);

    Task<MediaAssetDto?> GetByIdAsync(Guid mediaId, CancellationToken cancellationToken);

    Task<MediaAssetDto?> MarkAttachedAsync(
        Guid mediaId,
        string messageId,
        CancellationToken cancellationToken);

    Task<MediaAssetDto?> SoftDeleteAsync(Guid mediaId, CancellationToken cancellationToken);
}
