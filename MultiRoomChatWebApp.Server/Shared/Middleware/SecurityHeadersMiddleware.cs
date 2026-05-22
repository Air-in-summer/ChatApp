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

        // Cho phép avatar Google từ host cụ thể, không mở toàn bộ ảnh HTTPS bên ngoài.
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://lh3.googleusercontent.com; font-src 'self' data:;";

        await _next(context);
    }
}
