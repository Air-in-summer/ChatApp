using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Authentication;

/// <summary>
/// Xac dinh user id cho SignalR dua tren claim NameIdentifier.
/// </summary>
/// <remarks>
/// Luong xu ly:
/// 1. Authentication pipeline xac thuc request bang BFF session hoac Bearer fallback.
/// 2. Principal sau xac thuc phai co ClaimTypes.NameIdentifier la Guid cua user.
/// 3. SignalR dung gia tri nay lam Context.UserIdentifier de Clients.User/Clients.Users route dung ket noi.
///
/// Luu y:
/// - BFF session va JWT cu deu phai cung cap NameIdentifier de realtime routing khong doi contract.
/// - Tra ve null khi claim khong hop le de Hub bi xem la chua co user identifier.
/// </remarks>
public sealed class NameIdentifierUserIdProvider : IUserIdProvider
{
    /// <summary>
    /// Doc user id tu ClaimsPrincipal cua SignalR connection.
    /// </summary>
    /// <param name="connection">Thong tin ket noi SignalR hien tai.</param>
    /// <returns>Chuoi Guid user neu claim hop le; nguoc lai tra ve null.</returns>
    public string? GetUserId(HubConnectionContext connection)
    {
        var userId = connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var parsedUserId)
            ? parsedUserId.ToString()
            : null;
    }
}
