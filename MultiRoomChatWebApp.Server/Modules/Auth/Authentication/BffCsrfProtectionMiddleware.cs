using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Authentication;

/// <summary>
/// Bao ve unsafe browser request dung cookie bang antiforgery token va source-origin check.
/// </summary>
public sealed class BffCsrfProtectionMiddleware
{
    private static readonly HashSet<string> UnsafeMethods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            HttpMethods.Post,
            HttpMethods.Put,
            HttpMethods.Patch,
            HttpMethods.Delete
        };

    private static readonly HashSet<string> ProtectedAuthPaths =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/auth/login",
            "/api/auth/register",
            "/api/auth/logout",
            "/api/auth/session/renew"
        };

    private static readonly HashSet<string> ExemptPaths =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/auth/google/login",
            "/api/auth/google/callback",
            "/api/auth/google/complete",
            LiveKitWebhookPath
        };

    private static readonly HashSet<string> SourceOnlyPaths =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "/hub/chat/negotiate"
        };

    private const string LiveKitWebhookPath = "/api/v1/voice/livekit/webhook";

    private readonly RequestDelegate _next;
    private readonly IAntiforgery _antiforgery;
    private readonly BffAuthOptions _options;
    private readonly ILogger<BffCsrfProtectionMiddleware> _logger;
    private readonly HashSet<string> _configuredOrigins;

    public BffCsrfProtectionMiddleware(
        RequestDelegate next,
        IAntiforgery antiforgery,
        IOptions<BffAuthOptions> options,
        IConfiguration configuration,
        ILogger<BffCsrfProtectionMiddleware> logger)
    {
        _next = next;
        _antiforgery = antiforgery;
        _options = options.Value;
        _logger = logger;
        _configuredOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Select(NormalizeOrigin)
            .Where(origin => origin != null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldProtect(context.Request))
        {
            await _next(context);
            return;
        }

        ValidateRequestSource(context.Request);
        if (RequiresSourceOnlyProtection(context.Request.Path))
        {
            await _next(context);
            return;
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            _logger.LogWarning(
                "CSRF validation failed. Path={Path}; Origin={Origin}",
                context.Request.Path,
                GetSourceHeaderForLog(context.Request));

            throw ApiException.Forbidden(
                "csrf_validation_failed",
                "Yeu cau bao mat khong hop le. Vui long tai lai trang va thu lai.");
        }

        await _next(context);
    }

    private bool ShouldProtect(HttpRequest request)
    {
        if (!_options.Enabled ||
            !_options.RequireCsrf ||
            !UnsafeMethods.Contains(request.Method) ||
            IsExplicitlyExempt(request.Path))
        {
            return false;
        }

        if (RequiresSourceOnlyProtection(request.Path))
        {
            return true;
        }

        if (ProtectedAuthPaths.Contains(request.Path.Value ?? string.Empty))
        {
            return true;
        }

        if (!request.Cookies.ContainsKey(_options.CookieName))
        {
            return false;
        }

        return true;
    }

    private static bool IsExplicitlyExempt(PathString path)
    {
        return ExemptPaths.Contains(path.Value ?? string.Empty);
    }

    private static bool RequiresSourceOnlyProtection(PathString path)
    {
        return SourceOnlyPaths.Contains(path.Value ?? string.Empty);
    }

    private void ValidateRequestSource(HttpRequest request)
    {
        var sourceOrigin = GetSourceOrigin(request);
        if (sourceOrigin == null || !GetAllowedOrigins(request).Contains(sourceOrigin))
        {
            _logger.LogWarning(
                "CSRF source check failed. Path={Path}; Origin={Origin}",
                request.Path,
                GetSourceHeaderForLog(request));

            throw ApiException.Forbidden(
                "csrf_origin_invalid",
                "Nguon gui yeu cau khong duoc phep.");
        }
    }

    private HashSet<string> GetAllowedOrigins(HttpRequest request)
    {
        var allowedOrigins = new HashSet<string>(
            _configuredOrigins,
            StringComparer.OrdinalIgnoreCase);
        var currentOrigin = NormalizeOrigin($"{request.Scheme}://{request.Host}");

        if (currentOrigin != null)
        {
            allowedOrigins.Add(currentOrigin);
        }

        return allowedOrigins;
    }

    private static string? GetSourceOrigin(HttpRequest request)
    {
        var originHeader = request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(originHeader))
        {
            return NormalizeOrigin(originHeader);
        }

        var refererHeader = request.Headers.Referer.ToString();
        return string.IsNullOrWhiteSpace(refererHeader)
            ? null
            : NormalizeOrigin(refererHeader);
    }

    private static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static string GetSourceHeaderForLog(HttpRequest request)
    {
        return GetSourceOrigin(request) ?? "missing_or_invalid";
    }
}
