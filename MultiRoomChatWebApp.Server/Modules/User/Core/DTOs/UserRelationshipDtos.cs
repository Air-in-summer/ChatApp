using MultiRoomChatWebApp.Server.Modules.User.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

/// <summary>
/// Thong tin public toi thieu cua user trong relationship graph.
/// </summary>
public class UserRelationshipProfileDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// Ban be cua user hien tai kem thoi diem ket ban.
/// </summary>
public class FriendDto
{
    public UserRelationshipProfileDto User { get; set; } = new();
    public DateTime FriendsSince { get; set; }
}

/// <summary>
/// Loi moi ket ban kem profile cua hai dau request.
/// </summary>
public class FriendRequestDto
{
    public Guid Id { get; set; }
    public UserRelationshipProfileDto Requester { get; set; } = new();
    public UserRelationshipProfileDto Receiver { get; set; } = new();
    public FriendRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
}

/// <summary>
/// User da bi current user chan.
/// </summary>
public class BlockedUserDto
{
    public UserRelationshipProfileDto User { get; set; } = new();
    public DateTime BlockedAt { get; set; }
}

/// <summary>
/// Snapshot presence hien thi cho friend hop le.
/// </summary>
public class PresenceDto
{
    public Guid UserId { get; set; }
    public bool IsOnline { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

/// <summary>
/// Request tao loi moi ket ban toi mot user active.
/// </summary>
public class CreateFriendRequestRequest
{
    public Guid ReceiverId { get; set; }
}

/// <summary>
/// Request chan mot user active.
/// </summary>
public class BlockUserRequest
{
    public Guid BlockedUserId { get; set; }
}
