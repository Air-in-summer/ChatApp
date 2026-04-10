using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Hubs;

/// <summary>
/// Hub xử lý Websocket kết nối thời gian thực cho tính năng Chat.
/// Được gắn [Authorize] đảm bảo chỉ User gửi JWT token lên mới chui lọt.
/// </summary>
[Authorize]
public class ChatHub : Hub<IChatClient>
{
    private readonly IPresenceTracker _tracker;

    public ChatHub(IPresenceTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>
    /// Kích hoạt tự động khi 1 kết nối Websocket được thiết lập thành công.
    /// Ghi nhận Connection và tung tin báo hiệu lên luồng chung.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdString, out Guid currentUserId))
        {
            // Báo vào Tracker. Trả về True nếu đây là cửa sổ đầu tiên user này truy cập
            bool isOnline = await _tracker.UserConnected(currentUserId, Context.ConnectionId);

            if (isOnline)
            {
                // Báo cho MỌI NGƯỜI KHÁC biết ổng vừa lên mạng
                // Ở quy mô cực lớn có thể bị lag broadcast, lúc đó ta mới tối ưu báo cho list bạn bè thôi.
                await Clients.Others.UserIsOnline(currentUserId);
            }
        }

        // Bắt buộc gọi base method của Microsoft
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Kích hoạt khi tab trình duyệt đóng, user crash mạng, hoặc connection bị ngắt chủ động.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userIdString = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdString, out Guid currentUserId))
        {
            // Mất 1 kết nối (có thể user này tắt 1 tab nhưng vẫn đang mở tab khác)
            bool isOffline = await _tracker.UserDisconnected(currentUserId, Context.ConnectionId);

            if (isOffline)
            {
                // Nếu đây là cái phao cuối cùng -> Rụng hoàn toàn -> Broadcast cho all biết ổng sụp rồi
                await Clients.Others.UserIsOffline(currentUserId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
