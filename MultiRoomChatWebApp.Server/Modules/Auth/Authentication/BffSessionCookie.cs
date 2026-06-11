using MultiRoomChatWebApp.Server.Modules.Auth.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Authentication;

public static class BffSessionCookie
{
    /// <summary>
    /// Ghi opaque session token vao cookie theo cau hinh BFF.
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
    /// Xoa BFF session cookie voi cung path va security attributes luc tao.
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
