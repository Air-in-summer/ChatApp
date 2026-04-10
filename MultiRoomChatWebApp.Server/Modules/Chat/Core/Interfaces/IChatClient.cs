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
}
