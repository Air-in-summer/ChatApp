using MultiRoomChatWebApp.Server.Modules.Room.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.User.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

/// <summary>
/// Lưu trữ trạng thái "Đã xem" của người dùng đối với một phòng chat.
/// Bảng này được tách riêng khỏi RoomMembers để tránh làm phình bảng (bloat) và giảm tải MVCC do update liên tục.
/// </summary>
public class ReadReceipt
{
    public Guid UserId { get; set; }
    public MultiRoomChatWebApp.Server.Modules.User.Core.Entities.User User { get; set; } = null!;

    public Guid RoomId { get; set; }
    public MultiRoomChatWebApp.Server.Modules.Room.Core.Entities.Room Room { get; set; } = null!;

    /// <summary>
    /// ID của tin nhắn mới nhất trong MongoDB mà user này đã xem.
    /// Dạng string vì MongoDB sử dụng ObjectId.
    /// </summary>
    public string LastReadMessageId { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
