using MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

/// <summary>
/// Quan ly vong doi giu cho attachment giua luc tiep nhan va luc luu tin nhan.
/// </summary>
public interface IChatMediaReservationService
{
    /// <summary>
    /// Giu cho toan bo attachment trong mot transaction; khong bao gio giu mot phan tap tep.
    /// </summary>
    Task<ChatMediaReservationResult> ReserveAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        DateTime reservedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Bo giu cho neu enqueue that bai; chi tac dong den reservation cua dung messageId.
    /// </summary>
    Task ReleaseAsync(
        IReadOnlyCollection<Guid> mediaIds,
        string messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tai attachment da Reserved hoac da Attached vao cung tin nhan de worker xu ly idempotent.
    /// </summary>
    Task<IReadOnlyList<MediaAsset>?> LoadForPersistenceAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Chuyen reservation sang Attached trong mot transaction; retry cung messageId duoc coi la thanh cong.
    /// </summary>
    Task<bool> CompleteAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        DateTime attachedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hoan tat reservation va tra ket qua chi tiet de worker phan biet no-op voi xung dot nghiep vu.
    /// </summary>
    Task<ChatMediaCompletionResult> CompleteWithResultAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid ownerUserId,
        Guid roomId,
        string messageId,
        DateTime attachedAtUtc,
        CancellationToken cancellationToken);
}
