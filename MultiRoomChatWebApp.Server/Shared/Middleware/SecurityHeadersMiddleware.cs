namespace MultiRoomChatWebApp.Server.Shared.Middleware;

/// <summary>
/// Middleware bổ sung các HTTP headers bảo mật vào mọi response.
/// Bảo vệ app khỏi clickjacking, MIME sniffing, XSS, và rò rỉ thông tin.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Prevent clickjacking: disallow embedding in iframes
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // Prevent MIME type sniffing
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Control referrer information sent with requests
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Restrict browser features (camera, microphone allowed for voice chat)
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(self), geolocation=()";

        // Basic Content Security Policy
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:;";

        await _next(context);
    }
}
