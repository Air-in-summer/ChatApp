using MultiRoomChatWebApp.Server.Modules.Auth.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Authentication;

/// <summary>
/// Cung cấp các phương thức tiện ích để thao tác với HTTP Cookie chứa thông tin xác thực phiên (Session Token) trong mô hình BFF.
/// </summary>
public static class BffSessionCookie
{
    /// <summary>
    /// Đính kèm opaque session token vào HTTP response cookie dựa trên cấu hình bảo mật BFF.
    /// Thiết lập các cờ an toàn như HttpOnly, Secure và SameSite để phòng ngừa XSS và CSRF.
    /// </summary>
    public static void Append(
        HttpResponse response,
        BffAuthOptions options,
        string rawSessionToken,
        DateTime expiresAtUtc)
    {
        var expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero);
        var remaining = expires - DateTimeOffset.UtcNow;

        response.Cookies.Append(
            options.CookieName,
            rawSessionToken,
            new CookieOptions
            {
                HttpOnly = options.HttpOnly,
                Secure = options.Secure,
                SameSite = options.SameSite,
                Path = options.CookiePath,
                IsEssential = true,
                Expires = expires,
                MaxAge = remaining > TimeSpan.Zero
                    ? remaining
                    : TimeSpan.FromSeconds(1)
            });
    }

    /// <summary>
    /// Xóa cookie phiên xác thực hiện tại trên trình duyệt bằng cách đặt thời gian hết hạn trong quá khứ.
    /// Giữ nguyên các thuộc tính bảo mật (Path, Domain, SameSite) như lúc tạo để đảm bảo trình duyệt xóa đúng cookie.
    /// </summary>
    public static void Delete(
        HttpResponse response,
        BffAuthOptions options)
    {
        response.Cookies.Delete(
            options.CookieName,
            new CookieOptions
            {
                HttpOnly = options.HttpOnly,
                Secure = options.Secure,
                SameSite = options.SameSite,
                Path = options.CookiePath,
                IsEssential = true
            });
    }
}
