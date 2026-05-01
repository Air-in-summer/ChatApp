using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

public interface IChatService
{
    /// <summary>
    /// Lấy danh sách tin nhắn của một phòng chat theo cơ chế Cursor-based Pagination
    /// </summary>
    /// <param name="roomId">ID của phòng</param>
    /// <param name="cursor">ID của tin nhắn cũ nhất đang hiển thị (để lấy các tin cũ hơn). Null nếu là lần load đầu tiên.</param>
    /// <param name="limit">Số lượng tin nhắn tối đa cần lấy</param>
    /// <returns>Danh sách tin nhắn đã được sắp xếp từ cũ đến mới (để hiển thị trực tiếp lên UI)</returns>
    Task<IEnumerable<Message>> GetMessagesAsync(Guid roomId, string? cursor, int limit = 50);

    /// <summary>
    /// Lấy tin nhắn cuối cùng, số lượng tin chưa đọc và ID tin cuối cùng đã đọc của các phòng.
    /// </summary>
    Task<Dictionary<Guid, (Message? LastMessage, int UnreadCount, string? LastReadMessageId)>> GetRoomOverviewsAsync(Guid userId, List<Guid> roomIds);
}
