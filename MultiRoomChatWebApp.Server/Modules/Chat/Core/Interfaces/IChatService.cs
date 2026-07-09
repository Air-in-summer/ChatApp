using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

public interface IChatService
{
    /// <summary>
    /// Lấy danh sách tin nhắn của một phòng chat theo beforeMessageId.
    /// </summary>
    /// <param name="roomId">ID của phòng</param>
    /// <param name="cursor">Opaque cursor do API trả về. Null nếu là lần load đầu tiên.</param>
    /// <param name="limit">Số lượng tin nhắn tối đa cần lấy</param>
    /// <returns>Danh sách tin nhắn đã được sắp xếp từ cũ đến mới (để hiển thị trực tiếp lên UI)</returns>
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid roomId, string? beforeMessageId, int limit = 50);

    /// <summary>
    /// Lay cua so timeline quanh mot tin nhan dich trong phong.
    /// </summary>
    /// <param name="roomId">ID cua phong.</param>
    /// <param name="messageId">ID Mongo ObjectId cua tin nhan dich.</param>
    /// <param name="before">So tin truoc target can lay.</param>
    /// <param name="after">So tin sau target can lay.</param>
    /// <param name="cancellationToken">Token huy thao tac bat dong bo.</param>
    /// <returns>Cua so tin nhan theo thu tu cu den moi kem cursor hai phia.</returns>
    Task<MessageContextResponseDto> GetMessageContextAsync(
        Guid roomId,
        string messageId,
        int before = 20,
        int after = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tim kiem tin nhan co noi dung text trong mot phong.
    /// </summary>
    /// <param name="roomId">ID cua phong.</param>
    /// <param name="query">Tu khoa tim kiem.</param>
    /// <param name="page">Trang ket qua, bat dau tu 1.</param>
    /// <param name="pageSize">So ket qua moi trang.</param>
    /// <param name="cancellationToken">Token huy thao tac bat dong bo.</param>
    /// <returns>Danh sach ket qua tim kiem kem metadata phan trang.</returns>
    Task<MessageSearchResponseDto> SearchMessagesAsync(
        Guid roomId,
        string query,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy tin nhắn cuối cùng, số lượng tin chưa đọc và ID tin cuối cùng đã đọc của các phòng.
    /// </summary>
    Task<Dictionary<Guid, (Message? LastMessage, int UnreadCount, string? LastReadMessageId)>> GetRoomOverviewsAsync(Guid userId, List<Guid> roomIds);
}
