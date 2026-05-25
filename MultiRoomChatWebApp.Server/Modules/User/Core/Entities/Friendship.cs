using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

/// <summary>
/// Quan he ban be hai chieu, luu duy nhat mot ban ghi theo cap user da normalize.
/// </summary>
/// <remarks>
/// Quy uoc:
/// - UserAId luon nho hon UserBId theo thu tu Guid.
/// - "A la ban cua B" va "B la ban cua A" cung chi la mot ban ghi.
/// </remarks>
public class Friendship
{
    public Guid UserAId { get; set; }
    public virtual AppUser UserA { get; set; } = null!;

    public Guid UserBId { get; set; }
    public virtual AppUser UserB { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
