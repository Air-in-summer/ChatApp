namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Định nghĩa các methods từ Backend gọi ngược (Broadcast) về Frontend qua Websocket.
/// Giúp loại bỏ hoàn toàn việc xài "Magic String" khi code C#.
/// </summary>
public interface IChatClient
{
    /// <summary>
    /// Gửi thông báo người dùng đổi trạng thái cắm cờ Online.
    /// </summary>
    Task UserIsOnline(Guid userId);

    /// <summary>
    /// Gửi thông báo người dùng tắt hoàn toàn app/web - Offline.
    /// </summary>
    Task UserIsOffline(Guid userId);

    /// <summary>
    /// Frontend sẽ lắng nghe sự kiện này để in chử ra màn hình.
    /// </summary>
    Task ReceiveMessage(object message); 

    /// <summary>
    /// Nhận thông báo một user đang gõ phím.
    /// </summary>
    Task ReceiveTyping(Guid userId, Guid roomId);

    /// <summary>
    /// Nhận thông báo một user đã ngừng gõ phím.
    /// </summary>
    Task ReceiveTypingStopped(Guid userId, Guid roomId);

    /// <summary>
    /// Nhận thông báo một user đã xem tin nhắn đến ID nào trong phòng nào.
    /// roomId bắt buộc phải kèm theo để Frontend biết cập nhật đúng phòng.
    /// </summary>
    Task ReceiveReadReceipt(Guid userId, Guid roomId, string lastReadMessageId);

    /// <summary>
    /// Notify người gửi rằng tin nhắn của họ đã được Worker lưu thành công.
    /// Payload: tempId để FE tìm đúng tin tạm cần update.
    /// </summary>
    Task MessageStatusUpdated(string tempId, string finalMessageId, string status);
}
