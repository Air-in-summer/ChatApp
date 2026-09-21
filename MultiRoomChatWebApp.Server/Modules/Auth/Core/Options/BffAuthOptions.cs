using Microsoft.AspNetCore.Http;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Options;

/// <summary>
/// Các cấu hình liên quan đến bảo mật phiên đăng nhập, cookie, CORS và CSRF.
/// Áp dụng mẫu Options Pattern để lấy giá trị từ file cấu hình (appsettings.json).
/// </summary>
public sealed class BffAuthOptions
{
    public const string SectionName = "Auth:Bff";

    public bool Enabled { get; set; } = true;

    public bool RequireCsrf { get; set; }

    public string CsrfHeaderName { get; set; } = "X-CSRF-TOKEN";

    public string CsrfCookieName { get; set; } = "__Host-chatapp_csrf";

    public string CookieName { get; set; } = BffAuthDefaults.DefaultCookieName;

    public string CookiePath { get; set; } = "/";

    public bool HttpOnly { get; set; } = true;

    public bool Secure { get; set; } = true;

    public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;

    public int SessionLifetimeMinutes { get; set; } = 10080;

    public bool SlidingExpiration { get; set; } = true;

    public int RenewalThresholdMinutes { get; set; } = 1440;

    public int LastSeenUpdateIntervalMinutes { get; set; } = 5;

    public bool IsValid(out string error)
    {
        if (!Enabled)
        {
            error = "Auth:Bff:Enabled must be true because BFF session auth is the only application authentication scheme.";
            return false;
        }

        if (Enabled && !HttpOnly)
        {
            error = "Auth:Bff:HttpOnly must be true when BFF session auth is enabled.";
            return false;
        }

        if (Enabled && !Secure)
        {
            error = "Auth:Bff:Secure must be true when BFF session auth is enabled.";
            return false;
        }

        if (Enabled &&
            SameSite is not SameSiteMode.Lax and not SameSiteMode.Strict)
        {
            error = "Auth:Bff:SameSite must be Lax or Strict when BFF session auth is enabled.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(CookieName))
        {
            error = "Auth:Bff:CookieName is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(CsrfHeaderName) ||
            string.IsNullOrWhiteSpace(CsrfCookieName))
        {
            error = "Auth:Bff CSRF header and cookie names are required.";
            return false;
        }

        if (string.Equals(
                CookieName,
                CsrfCookieName,
                StringComparison.OrdinalIgnoreCase))
        {
            error = "BFF session and CSRF cookies must use different names.";
            return false;
        }

        if (CookieName.StartsWith("__Host-", StringComparison.Ordinal) &&
            (!Secure || CookiePath != "/"))
        {
            error = "__Host- cookies require Secure=true and Path=/.";
            return false;
        }

        if (CsrfCookieName.StartsWith("__Host-", StringComparison.Ordinal) &&
            (!Secure || CookiePath != "/"))
        {
            error = "__Host- CSRF cookies require Secure=true and Path=/.";
            return false;
        }

        if (SessionLifetimeMinutes <= 0)
        {
            error = "Auth:Bff:SessionLifetimeMinutes must be greater than zero.";
            return false;
        }

        if (RenewalThresholdMinutes <= 0 ||
            RenewalThresholdMinutes >= SessionLifetimeMinutes)
        {
            error = "Auth:Bff:RenewalThresholdMinutes must be between zero and the session lifetime.";
            return false;
        }

        if (LastSeenUpdateIntervalMinutes <= 0)
        {
            error = "Auth:Bff:LastSeenUpdateIntervalMinutes must be greater than zero.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
