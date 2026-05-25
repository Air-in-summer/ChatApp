using AppUser = MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User;
using MultiRoomChatWebApp.Server.Modules.User.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

/// <summary>
/// Loi moi ket ban mot chieu, cho receiver chap nhan hoac tu choi.
/// </summary>
public class FriendRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequesterId { get; set; }
    public virtual AppUser Requester { get; set; } = null!;

    public Guid ReceiverId { get; set; }
    public virtual AppUser Receiver { get; set; } = null!;

    public FriendRequestStatus Status { get; set; } = FriendRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAt { get; set; }

    public DateTime? CanceledAt { get; set; }
}
