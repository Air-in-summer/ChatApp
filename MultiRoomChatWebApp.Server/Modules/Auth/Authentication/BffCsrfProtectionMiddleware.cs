using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Authentication;

/// <summary>
/// Đảm bảo tính bảo mật cho mô hình BFF (Backend-For-Frontend) bằng cách ngăn chặn các cuộc tấn công CSRF (Cross-Site Request Forgery).
/// Trong mô hình BFF, cookie phiên đăng nhập tự động được đính kèm vào mọi request. 
/// Middleware này có nhiệm vụ kiểm tra tất cả các HTTP request có nguy cơ thay đổi dữ liệu (POST, PUT, DELETE), 
/// nhằm xác minh nguồn gốc của request (thông qua Origin Header). 
/// Yêu cầu sẽ bị từ chối với mã lỗi 403 Forbidden nếu không xuất phát từ nguồn Frontend đã được cấp phép.
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

    /// <summary>
    /// Đánh chặn và xử lý HTTP Request đầu vào.
    /// Xác minh tính hợp lệ của Request trước khi chuyển tiếp đến các thành phần tiếp theo trong pipeline (Controller).
    /// Nếu phương thức HTTP an toàn hoặc đã qua kiểm tra thành công, gọi _next(context) để tiếp tục luồng xử lý.
    /// Nếu phát hiện dấu hiệu vi phạm bảo mật, luồng xử lý sẽ bị hủy bỏ và ngoại lệ sẽ được ném ra để trả về lỗi cho client.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Bước 1: Bộ lọc nhanh - Bỏ qua các request vô hại (GET) hoặc các API đặc thù (Webhook).
        if (!ShouldProtect(context.Request))
        {
            await _next(context);
            return;
        }

        // Bước 2: Kiểm tra nguồn gốc vật lý của Request (Origin / Referer Header).
        // Đảm bảo request xuất phát từ tên miền Frontend hợp lệ (đã config trong appsettings).
        ValidateRequestSource(context.Request);
        if (RequiresSourceOnlyProtection(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Bước 3: Xác thực chữ ký Anti-Forgery (Token X-CSRF) để phòng ngừa thêm 1 lớp.
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

    /// <summary>
    /// Đánh giá xem yêu cầu HTTP có cần được bảo vệ bởi cơ chế CSRF hay không.
    /// Phương thức trả về false (bỏ qua kiểm tra) trong các trường hợp:
    /// - Phương thức an toàn (GET, HEAD) vì không thay đổi trạng thái hệ thống.
    /// - Webhook hoặc các điểm cuối API đặc thù không sử dụng Cookie để xác thực (ví dụ: LiveKit).
    /// - Không có Cookie xác thực được gửi kèm trong yêu cầu, không tiềm ẩn rủi ro tấn công.
    /// </summary>
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

    /// <summary>
    /// Đối chiếu Origin Header của trình duyệt gửi lên với danh sách các miền hợp lệ được cấu hình (Cors:AllowedOrigins).
    /// Đây là chốt chặn quan trọng nhằm xác định nguồn gốc vật lý của request. 
    /// Nếu Origin Header không nằm trong danh sách cho phép, yêu cầu sẽ bị chặn lại để phòng ngừa việc 
    /// thao tác dữ liệu độc hại từ các trang web giả mạo.
    /// </summary>
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
