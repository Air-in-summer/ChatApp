using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Authentication;

/// <summary>
/// Trích xuất định danh người dùng (User ID) cho các kết nối SignalR dựa trên claim NameIdentifier.
/// </summary>
/// <remarks>
/// Quy trình xử lý:
/// 1. Authentication pipeline xác thực HTTP request bằng BFF Session Cookie (hoặc JWT).
/// 2. ClaimsPrincipal sinh ra sau khi xác thực bắt buộc chứa ClaimTypes.NameIdentifier mang giá trị Guid của user.
/// 3. SignalR sử dụng provider này để gán giá trị vào Context.UserIdentifier, phục vụ cho việc định tuyến tin nhắn
///    realtime (ví dụ: Clients.User(userId)).
///
/// Lưu ý: Trả về null nếu claim không hợp lệ hoặc không tồn tại, khi đó SignalR hub sẽ xử lý như kết nối ẩn danh (hoặc từ chối).
/// </remarks>
public sealed class NameIdentifierUserIdProvider : IUserIdProvider
{
    /// <summary>
    /// Đọc User ID từ ClaimsPrincipal của kết nối SignalR hiện hành.
    /// </summary>
    /// <param name="connection">Thông tin ngữ cảnh kết nối SignalR.</param>
    /// <returns>Chuỗi Guid đại diện cho người dùng nếu claim hợp lệ; ngược lại trả về null.</returns>
    public string? GetUserId(HubConnectionContext connection)
    {
        var userId = connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var parsedUserId)
            ? parsedUserId.ToString()
            : null;
    }
}
