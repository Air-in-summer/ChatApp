using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Options;

namespace MultiRoomChatWebApp.Server.Shared.Middleware;

/// <summary>
/// Middleware bo sung cac HTTP security headers cho browser response.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly MediaStorageOptions _mediaStorageOptions;
    private readonly SecurityHeadersOptions _securityHeadersOptions;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IOptions<MediaStorageOptions> mediaStorageOptions,
        IOptions<SecurityHeadersOptions> securityHeadersOptions)
    {
        _next = next;
        _configuration = configuration;
        _mediaStorageOptions = mediaStorageOptions.Value;
        _securityHeadersOptions = securityHeadersOptions.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=()";

        if (_securityHeadersOptions.CspEnabled)
        {
            var headerName = _securityHeadersOptions.CspReportOnly
                ? "Content-Security-Policy-Report-Only"
                : "Content-Security-Policy";

            context.Response.Headers[headerName] = BuildContentSecurityPolicy(context);
        }

        await _next(context);
    }

    private string BuildContentSecurityPolicy(HttpContext context)
    {
        var mediaSources = GetMediaOrigins();
        var liveKitSources = GetLiveKitOrigins();
        var corsSources = GetCorsOrigins();
        var currentOrigin = GetOrigin($"{context.Request.Scheme}://{context.Request.Host}");

        var imgSrc = BuildSourceList(
            "'self'",
            "data:",
            "blob:",
            "https://lh3.googleusercontent.com",
            mediaSources,
            _securityHeadersOptions.AdditionalImgSrc);

        var mediaSrc = BuildSourceList(
            "'self'",
            "blob:",
            mediaSources,
            _securityHeadersOptions.AdditionalMediaSrc);

        var connectSrc = BuildSourceList(
            "'self'",
            "ws:",
            "wss:",
            currentOrigin,
            corsSources,
            liveKitSources,
            mediaSources,
            _securityHeadersOptions.AdditionalConnectSrc);

        var frameAncestors = BuildSourceList(_securityHeadersOptions.FrameAncestors);

        return string.Join("; ", new[]
        {
            "default-src 'self'",
            "base-uri 'self'",
            "object-src 'none'",
            $"frame-ancestors {frameAncestors}",
            "form-action 'self'",
            "script-src 'self'",
            "style-src 'self' 'unsafe-inline'",
            $"img-src {imgSrc}",
            $"media-src {mediaSrc}",
            "font-src 'self' data:",
            $"connect-src {connectSrc}"
        });
    }

    private string[] GetMediaOrigins()
    {
        return new[]
            {
                _mediaStorageOptions.PublicEndpoint,
                _mediaStorageOptions.Endpoint
            }
            .Select(GetOrigin)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] GetLiveKitOrigins()
    {
        var origin = GetOrigin(_configuration["LiveKit:Host"]);
        if (string.IsNullOrWhiteSpace(origin))
            return [];

        var httpEquivalent = origin.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            ? $"http://{origin["ws://".Length..]}"
            : origin.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
                ? $"https://{origin["wss://".Length..]}"
                : origin;

        return new[] { origin, httpEquivalent }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] GetCorsOrigins()
    {
        return _configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Select(GetOrigin)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static string BuildSourceList(params object?[] sourceGroups)
    {
        return string.Join(
            ' ',
            sourceGroups
                .SelectMany(FlattenSources)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> FlattenSources(object? sourceGroup)
    {
        return sourceGroup switch
        {
            null => [],
            string source => [source],
            IEnumerable<string> sources => sources,
            _ => []
        };
    }

    private static string GetOrigin(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : string.Empty;
    }
}
