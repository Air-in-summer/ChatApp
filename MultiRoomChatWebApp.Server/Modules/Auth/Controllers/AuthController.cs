using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Auth.Authentication;
using MultiRoomChatWebApp.Server.Modules.Auth.Core;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Exceptions;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AuthLimit")]
public class AuthController : ControllerBase
{
    private const string DefaultRefreshTokenCookieName = "refreshToken";
    private const string DefaultRefreshTokenCookiePath = "/api/auth";
    private const int DefaultRefreshTokenMinutes = 10080;
    private const string GoogleExternalCookieScheme = "GoogleExternal";
    private const string GoogleOAuthCompletePath = "/api/auth/google/complete";

    private readonly IAuthService _authService;
    private readonly IAuthSessionService _authSessionService;
    private readonly IAntiforgery _antiforgery;
    private readonly BffAuthOptions _bffOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IAuthSessionService authSessionService,
        IAntiforgery antiforgery,
        IOptions<BffAuthOptions> bffOptions,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _authSessionService = authSessionService;
        _antiforgery = antiforgery;
        _bffOptions = bffOptions.Value;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Helper: Đặt Refresh Token vào HttpOnly Cookie trên Response.
    /// </summary>
    /// <param name="refreshToken">Giá trị Refresh Token cần lưu vào Cookie</param>
    /// <remarks>
    /// Cấu hình Cookie đảm bảo tuyệt đối an toàn:
    /// - HttpOnly = true : JavaScript không thể đọc được → chống XSS ăn cắp token.
    /// - Secure = true   : Chỉ gửi qua HTTPS → chống nghe lén trên đường truyền.
    /// - SameSite = Lax  : Không gửi trong POST/PUT/DELETE cross-site → chống CSRF cơ bản.
    ///                     Cho phép gửi khi người dùng click link navigation → UX tốt hơn.
    /// - MaxAge = 7 ngày : Khớp với thời hạn RefreshToken trong DB.
    /// </remarks>
    private CookieOptions GetBaseCookieOptions()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = _configuration.GetValue("Auth:RefreshTokenCookie:HttpOnly", true),
            Secure = _configuration.GetValue("Auth:RefreshTokenCookie:Secure", true),
            SameSite = GetRefreshCookieSameSite(),
            Path = GetRefreshCookiePath()
        };

        var domain = _configuration["Auth:RefreshTokenCookie:Domain"];
        if (!string.IsNullOrWhiteSpace(domain))
            cookieOptions.Domain = domain;

        return cookieOptions;
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        var options = GetBaseCookieOptions();
        options.MaxAge = TimeSpan.FromMinutes(GetRefreshTokenMinutes());
        Response.Cookies.Append(GetRefreshCookieName(), refreshToken, options);
    }

    /// <summary>
    /// Lấy tên cookie refresh token từ cấu hình để các thao tác set/read/delete đồng bộ.
    /// </summary>
    private string GetRefreshCookieName()
    {
        return _configuration["Auth:RefreshTokenCookie:Name"] ?? DefaultRefreshTokenCookieName;
    }

    /// <summary>
    /// Lấy Path cookie refresh token, mặc định chỉ gửi cookie đến auth endpoints.
    /// </summary>
    private string GetRefreshCookiePath()
    {
        return _configuration["Auth:RefreshTokenCookie:Path"] ?? DefaultRefreshTokenCookiePath;
    }

    /// <summary>
    /// Lấy SameSite từ cấu hình, fallback Lax để giảm rủi ro CSRF cho request dùng cookie.
    /// </summary>
    private SameSiteMode GetRefreshCookieSameSite()
    {
        var sameSite = _configuration["Auth:RefreshTokenCookie:SameSite"];
        return Enum.TryParse<SameSiteMode>(sameSite, ignoreCase: true, out var parsed)
            ? parsed
            : SameSiteMode.Lax;
    }

    /// <summary>
    /// Lấy thời hạn refresh token từ cấu hình để cookie MaxAge khớp với DB token lifetime.
    /// </summary>
    private int GetRefreshTokenMinutes()
    {
        return _configuration.GetValue("Auth:TokenLifetime:RefreshTokenMinutes", DefaultRefreshTokenMinutes);
    }

    private AuthSessionMetadata GetAuthSessionMetadata()
    {
        return new AuthSessionMetadata(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString());
    }

    private string? GetBffSessionToken()
    {
        return Request.Cookies[_bffOptions.CookieName];
    }

    private async Task ReplaceBffSessionAsync(Guid userId)
    {
        if (!_bffOptions.Enabled)
        {
            return;
        }

        var currentSessionToken = GetBffSessionToken();
        if (!string.IsNullOrWhiteSpace(currentSessionToken))
        {
            await _authSessionService.RevokeSessionAsync(
                currentSessionToken,
                "replaced_by_new_login",
                HttpContext.RequestAborted);
        }

        await CreateAndSetBffSessionAsync(userId);
    }

    private async Task EnsureBffSessionAsync(Guid userId)
    {
        if (!_bffOptions.Enabled)
        {
            return;
        }

        var currentSessionToken = GetBffSessionToken();
        if (!string.IsNullOrWhiteSpace(currentSessionToken))
        {
            var currentSession = await _authSessionService.ValidateSessionAsync(
                currentSessionToken,
                HttpContext.RequestAborted);

            if (currentSession?.UserId == userId)
            {
                return;
            }

            if (currentSession != null)
            {
                await _authSessionService.RevokeSessionAsync(
                    currentSessionToken,
                    "replaced_during_legacy_refresh",
                    HttpContext.RequestAborted);
            }
        }

        await CreateAndSetBffSessionAsync(userId);
    }

    private async Task CreateAndSetBffSessionAsync(Guid userId)
    {
        var session = await _authSessionService.CreateSessionAsync(
            userId,
            GetAuthSessionMetadata(),
            HttpContext.RequestAborted);

        BffSessionCookie.Append(
            Response,
            _bffOptions,
            session.RawSessionToken,
            session.ExpiresAtUtc);
    }

    private static AuthSessionResponse CreateAuthSessionResponse(
        AuthenticateResult authenticateResult)
    {
        var principal = authenticateResult.Principal;
        var expiresAtUtc = authenticateResult.Properties?.ExpiresUtc?.UtcDateTime;

        if (principal == null ||
            !CurrentUserClaims.TryGetUserId(principal, out var userId) ||
            expiresAtUtc == null)
        {
            throw ApiException.Unauthorized(
                "bff_session_invalid",
                "Phien dang nhap khong hop le.");
        }

        var username = GetClaimValue(
            principal,
            JwtRegisteredClaimNames.Name,
            ClaimTypes.Name) ?? userId.ToString();
        var displayName = GetClaimValue(
            principal,
            "displayName",
            ClaimTypes.Name,
            JwtRegisteredClaimNames.Name) ?? username;
        var avatarUrl = GetClaimValue(principal, "avatarUrl");

        return new AuthSessionResponse(
            userId,
            username,
            displayName,
            string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl,
            expiresAtUtc.Value);
    }

    /// <summary>
    /// Chuẩn hóa returnUrl để OAuth chỉ redirect về đường dẫn nội bộ của SPA.
    /// </summary>
    /// <param name="returnUrl">Đường dẫn frontend muốn quay lại sau khi đăng nhập xong.</param>
    /// <returns>Đường dẫn nội bộ an toàn, fallback về "/" nếu input không hợp lệ.</returns>
    private static string NormalizeInternalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/";

        var trimmed = returnUrl.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//") || trimmed.Contains('\\'))
            return "/";

        return trimmed;
    }

    /// <summary>
    /// Tạo URL frontend callback sau khi backend xử lý OAuth xong hoặc gặp lỗi.
    /// </summary>
    /// <param name="oauthError">Mã lỗi an toàn để frontend map thành message.</param>
    /// <param name="returnUrl">Đường dẫn nội bộ đã được normalize.</param>
    /// <returns>URL frontend callback kèm oauthError và returnUrl.</returns>
    private string BuildFrontendOAuthCallbackUrl(string oauthError, string returnUrl)
    {
        var frontendCallbackUrl = _configuration["Authentication:Google:FrontendCallbackUrl"];
        if (string.IsNullOrWhiteSpace(frontendCallbackUrl))
            frontendCallbackUrl = "/oauth/callback";

        var separator = frontendCallbackUrl.Contains('?') ? '&' : '?';
        return $"{frontendCallbackUrl}{separator}oauthError={Uri.EscapeDataString(oauthError)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    /// <summary>
    /// Tạo URL frontend callback thành công, không đưa access token vào query string.
    /// </summary>
    /// <param name="returnUrl">Đường dẫn nội bộ đã được normalize.</param>
    /// <returns>URL frontend callback chỉ kèm returnUrl an toàn.</returns>
    private string BuildFrontendOAuthSuccessCallbackUrl(string returnUrl)
    {
        var frontendCallbackUrl = _configuration["Authentication:Google:FrontendCallbackUrl"];
        if (string.IsNullOrWhiteSpace(frontendCallbackUrl))
            frontendCallbackUrl = "/oauth/callback";

        var separator = frontendCallbackUrl.Contains('?') ? '&' : '?';
        return $"{frontendCallbackUrl}{separator}returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    /// <summary>
    /// Đọc claim đầu tiên có giá trị từ principal Google.
    /// </summary>
    /// <param name="principal">ClaimsPrincipal đã được Google middleware tạo.</param>
    /// <param name="claimTypes">Danh sách claim type fallback theo thứ tự ưu tiên.</param>
    /// <returns>Giá trị claim nếu có, ngược lại null.</returns>
    private static string? GetClaimValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Kiểm tra claim email_verified từ Google.
    /// </summary>
    /// <param name="principal">ClaimsPrincipal đã được Google middleware tạo.</param>
    /// <returns>true nếu Google xác nhận email đã verified.</returns>
    private static bool IsGoogleEmailVerified(ClaimsPrincipal principal)
    {
        var value = GetClaimValue(principal, "email_verified", "urn:google:email_verified");
        return bool.TryParse(value, out var parsed) && parsed;
    }

    /// <summary>
    /// Hash provider user id để log audit mà không ghi raw external identifier.
    /// </summary>
    /// <param name="provider">Tên OAuth provider.</param>
    /// <param name="providerUserId">User id gốc từ OAuth provider.</param>
    /// <returns>Hash rút gọn đủ để correlation log, không dùng làm security token.</returns>
    private static string? HashProviderUserIdForAudit(string provider, string? providerUserId)
    {
        if (string.IsNullOrWhiteSpace(providerUserId))
            return null;

        var raw = Encoding.UTF8.GetBytes($"{provider}:{providerUserId}");
        var hash = SHA256.HashData(raw);
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Log kết quả OAuth thất bại với reason an toàn, không log token/claim nhạy cảm.
    /// </summary>
    private void LogOAuthFailure(string provider, string reason, string? providerUserId = null)
    {
        var providerUserIdHash = HashProviderUserIdForAudit(provider, providerUserId);
        _logger.LogWarning(
            "Đăng nhập OAuth thất bại. Provider={Provider}; Reason={Reason}; ProviderUserIdHash={ProviderUserIdHash}",
            provider,
            reason,
            providerUserIdHash ?? "unknown");
    }

    /// <summary>
    /// Log kết quả OAuth thành công theo user nội bộ và hash provider user id.
    /// </summary>
    private void LogOAuthSuccess(string provider, Guid userId, string providerUserId)
    {
        _logger.LogInformation(
            "Đăng nhập OAuth thành công. Provider={Provider}; UserId={UserId}; ProviderUserIdHash={ProviderUserIdHash}",
            provider,
            userId,
            HashProviderUserIdForAudit(provider, providerUserId));
    }

    /// <summary>
    /// [GET] /api/auth/google/login - Bắt đầu flow đăng nhập Google.
    /// </summary>
    /// <param name="returnUrl">Đường dẫn nội bộ để frontend quay lại sau khi OAuth thành công.</param>
    /// <returns>ChallengeResult để ASP.NET Core redirect sang Google.</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Nhận returnUrl từ frontend và chuẩn hóa để chống open redirect.
    /// 2. Tạo RedirectUri nội bộ sau khi Google middleware xử lý callback xong.
    /// 3. Challenge sang Google bằng provider scheme, không đưa token Google về frontend.
    /// </remarks>
    [HttpGet("google/login")]
    public IActionResult GoogleLogin([FromQuery] string? returnUrl)
    {
        var safeReturnUrl = NormalizeInternalReturnUrl(returnUrl);
        var completeRedirectUri = $"{GoogleOAuthCompletePath}{QueryString.Create("returnUrl", safeReturnUrl)}";

        var properties = new AuthenticationProperties
        {
            RedirectUri = completeRedirectUri
        };
        properties.Items["returnUrl"] = safeReturnUrl;

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// [GET] /api/auth/google/complete - Đọc principal Google sau khi middleware xử lý callback.
    /// </summary>
    /// <param name="returnUrl">Đường dẫn nội bộ để frontend quay lại sau khi OAuth hoàn tất.</param>
    /// <returns>Redirect về frontend callback với kết quả OAuth hiện tại.</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Đọc external cookie `GoogleExternal` do Google middleware tạo sau callback.
    /// 2. Lấy và validate provider user id, email, email_verified, displayName, picture.
    /// 3. Xóa external cookie tạm để không giữ principal Google dài hơn cần thiết.
    /// 4. Nếu login thành công thì phát BFF session, giữ refresh cookie legacy trong giai đoạn migration và redirect frontend callback thành công.
    /// 5. Nếu email đã tồn tại nhưng chưa link thì trả `account_conflict`, không auto-link.
    /// </remarks>
    [HttpGet("google/complete")]
    public async Task<IActionResult> GoogleComplete([FromQuery] string? returnUrl)
    {
        var safeReturnUrl = NormalizeInternalReturnUrl(returnUrl);
        const string provider = GoogleDefaults.AuthenticationScheme;
        var authenticateResult = await HttpContext.AuthenticateAsync(GoogleExternalCookieScheme);

        try
        {
            if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
            {
                LogOAuthFailure(provider, "oauth_not_completed");
                return Redirect(BuildFrontendOAuthCallbackUrl("oauth_failed", safeReturnUrl));
            }

            var principal = authenticateResult.Principal;
            var providerUserId = GetClaimValue(principal, ClaimTypes.NameIdentifier);
            var email = GetClaimValue(principal, ClaimTypes.Email);
            var displayName = GetClaimValue(principal, ClaimTypes.Name);
            var picture = GetClaimValue(principal, "picture", "urn:google:picture", "urn:google:image");

            if (string.IsNullOrWhiteSpace(providerUserId) || string.IsNullOrWhiteSpace(email))
            {
                LogOAuthFailure(provider, "missing_required_claims", providerUserId);
                return Redirect(BuildFrontendOAuthCallbackUrl("oauth_failed", safeReturnUrl));
            }

            if (!IsGoogleEmailVerified(principal))
            {
                LogOAuthFailure(provider, "email_not_verified", providerUserId);
                return Redirect(BuildFrontendOAuthCallbackUrl("email_not_verified", safeReturnUrl));
            }

            var normalizedEmail = email.Trim().ToLowerInvariant();
            ExternalLoginAuthResult authResult;
            try
            {
                authResult = await _authService.LoginWithExternalProviderAsync(new ExternalLoginRequest(
                    provider,
                    providerUserId,
                    normalizedEmail,
                    displayName,
                    picture));
            }
            catch (ApiException ex) when (ex.Code == "user_inactive")
            {
                LogOAuthFailure(provider, "user_inactive", providerUserId);
                return Redirect(BuildFrontendOAuthCallbackUrl("user_inactive", safeReturnUrl));
            }
            catch (UnauthorizedAccessException)
            {
                LogOAuthFailure(provider, "user_inactive", providerUserId);
                return Redirect(BuildFrontendOAuthCallbackUrl("user_inactive", safeReturnUrl));
            }

            if (authResult.Status == ExternalLoginAuthStatus.AccountConflict)
            {
                LogOAuthFailure(provider, "account_conflict", providerUserId);
                return Redirect(BuildFrontendOAuthCallbackUrl("account_conflict", safeReturnUrl));
            }

            if (authResult.AuthResponse == null)
            {
                LogOAuthFailure(provider, "oauth_failed", providerUserId);
                return Redirect(BuildFrontendOAuthCallbackUrl("oauth_failed", safeReturnUrl));
            }

            await ReplaceBffSessionAsync(authResult.AuthResponse.UserId);
            SetRefreshTokenCookie(authResult.AuthResponse.RefreshToken);
            LogOAuthSuccess(provider, authResult.AuthResponse.UserId, providerUserId);

            return Redirect(BuildFrontendOAuthSuccessCallbackUrl(safeReturnUrl));
        }
        finally
        {
            await HttpContext.SignOutAsync(GoogleExternalCookieScheme);
        }
    }

    /// <summary>
    /// [POST] /api/auth/register - Đăng ký tài khoản người dùng mới
    /// </summary>
    /// <param name="request">Bao gồm Username, DisplayName, Email, và Password</param>
    /// <returns>AuthClientResponse chứa Access Token và thông tin user (không có Refresh Token)</returns>
    /// <remarks>
    /// Request:
    /// - Body: RegisterRequest { Username, DisplayName, Email, Password }
    /// 
    /// Response Success (200):
    /// {
    ///   "accessToken": "eyJ...",
    ///   "userId": "guid...",
    ///   "username": "alice",
    ///   "displayName": "Alice"
    /// }
    /// 
    /// Response Error:
    /// - 400: Email hoặc Username đã tồn tại trong hệ thống.
    /// 
    /// Side effects:
    /// - Tạo user mới trong DB và băm mật khẩu bằng BCrypt.
    /// - Phát sinh một Refresh Token lưu vào CSDL và gắn vào HttpOnly Cookie.
    /// </remarks>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        await ReplaceBffSessionAsync(result.UserId);

        // Lưu RefreshToken vào HttpOnly Cookie (ẩn khỏi JavaScript)
        SetRefreshTokenCookie(result.RefreshToken);

        // Chỉ trả về AccessToken + User info, KHÔNG trả RefreshToken trong body
        return Ok(new AuthClientResponse(
            result.AccessToken,
            result.UserId,
            result.Username,
            result.DisplayName,
            result.AvatarUrl));
    }

    /// <summary>
    /// [POST] /api/auth/login - Đăng nhập vào hệ thống
    /// </summary>
    /// <param name="request">Bao gồm Email và Password</param>
    /// <returns>AuthClientResponse chứa Access Token và thông tin user (không có Refresh Token)</returns>
    /// <remarks>
    /// Request:
    /// - Body: LoginRequest { Email, Password }
    /// 
    /// Response Success (200):
    /// Trả về accessToken (15m). Refresh Token (7 ngày) được lưu vào HttpOnly Cookie.
    /// 
    /// Response Error:
    /// - 401: Sai email, sai mật khẩu, hoặc tài khoản đã bị khóa (IsActive = false).
    /// </remarks>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        await ReplaceBffSessionAsync(result.UserId);

        // Lưu RefreshToken vào HttpOnly Cookie (ẩn khỏi JavaScript)
        SetRefreshTokenCookie(result.RefreshToken);

        // Chỉ trả về AccessToken + User info, KHÔNG trả RefreshToken trong body
        return Ok(new AuthClientResponse(
            result.AccessToken,
            result.UserId,
            result.Username,
            result.DisplayName,
            result.AvatarUrl));
    }

    /// <summary>
    /// [POST] /api/auth/refresh - Cấp lại Access Token mới bằng Refresh Token
    /// </summary>
    /// <returns>AuthClientResponse với Access Token mới nhất (Rotation)</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Đọc Refresh Token từ HttpOnly Cookie "refreshToken" (không nhận từ body nữa).
    /// 2. Nếu không có Cookie → trả về 401 Unauthorized.
    /// 3. Gọi AuthService để xác minh và đổi token mới (Rotation).
    /// 4. Ghi đè Cookie cũ = Cookie mới (refresh token rotation).
    /// 5. Trả về Access Token mới cho Client.
    /// 
    /// Response Error:
    /// - 401: Cookie không tồn tại, hoặc Refresh Token không hợp lệ/đã thu hồi/hết hạn.
    /// 
    /// Side effects:
    /// - Token cũ sẽ bị đánh dấu IsRevoked = true để tránh dùng lại (chống Replay Attack).
    /// </remarks>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        // Đọc RefreshToken từ Cookie thay vì từ body JSON
        var refreshTokenFromCookie = Request.Cookies[GetRefreshCookieName()];
        if (string.IsNullOrEmpty(refreshTokenFromCookie))
            throw ApiException.Unauthorized("refresh_token_missing", "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");

        var result = await _authService.RefreshAsync(refreshTokenFromCookie);
        await EnsureBffSessionAsync(result.UserId);

        // Ghi đè token mới vào Cookie (Token Rotation)
        SetRefreshTokenCookie(result.RefreshToken);

        return Ok(new AuthClientResponse(
            result.AccessToken,
            result.UserId,
            result.Username,
            result.DisplayName,
            result.AvatarUrl));
    }

    /// <summary>
    /// [POST] /api/auth/logout - Khóa phiên đăng nhập hiện tại
    /// </summary>
    /// <returns>Trả về kết quả 200 OK khi đăng xuất xong</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Đọc Refresh Token từ HttpOnly Cookie "refreshToken".
    /// 2. Nếu không có Cookie → vẫn xóa Cookie và trả về 200 (idempotent).
    /// 3. Gọi AuthService để thu hồi token trong DB.
    /// 4. Xóa Cookie khỏi trình duyệt.
    /// 
    /// Side effects:
    /// - Refresh Token bị đánh dấu IsRevoked = true trong DB.
    /// - Cookie "refreshToken" bị xóa khỏi trình duyệt Client.
    /// </remarks>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshTokenFromCookie = Request.Cookies[GetRefreshCookieName()];
        var bffSessionToken = GetBffSessionToken();

        // Nếu có token trong Cookie thì thu hồi trong DB
        if (!string.IsNullOrEmpty(refreshTokenFromCookie))
            await _authService.LogoutAsync(refreshTokenFromCookie);

        // Xóa Cookie khỏi trình duyệt dù token có tồn tại hay không (idempotent)
        if (!string.IsNullOrWhiteSpace(bffSessionToken))
        {
            await _authSessionService.RevokeSessionAsync(
                bffSessionToken,
                "user_logout",
                HttpContext.RequestAborted);
        }

        Response.Cookies.Delete(GetRefreshCookieName(), GetBaseCookieOptions());
        BffSessionCookie.Delete(Response, _bffOptions);

        return Ok();
    }

    /// <summary>
    /// Tra thong tin BFF session hien tai, khong tra access token hay raw session token.
    /// </summary>
    [HttpGet("session")]
    [ProducesResponseType(typeof(AuthSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSession()
    {
        var authenticateResult = await HttpContext.AuthenticateAsync(
            BffAuthDefaults.SessionScheme);

        if (!authenticateResult.Succeeded)
        {
            throw ApiException.Unauthorized(
                "bff_session_invalid",
                "Phien dang nhap da het han. Vui long dang nhap lai.");
        }

        return Ok(CreateAuthSessionResponse(authenticateResult));
    }

    /// <summary>
    /// Gia han BFF session hien tai ma khong phat access token.
    /// </summary>
    [HttpPost("session/renew")]
    [ProducesResponseType(typeof(AuthSessionRenewalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RenewSession()
    {
        if (!_bffOptions.Enabled)
        {
            throw ApiException.Unauthorized(
                "bff_session_disabled",
                "BFF session chua duoc bat.");
        }

        var rawSessionToken = GetBffSessionToken();
        var renewal = await _authSessionService.RenewSessionAsync(
            rawSessionToken,
            HttpContext.RequestAborted);

        if (renewal == null || string.IsNullOrWhiteSpace(rawSessionToken))
        {
            throw ApiException.Unauthorized(
                "bff_session_invalid",
                "Phien dang nhap da het han. Vui long dang nhap lai.");
        }

        BffSessionCookie.Append(
            Response,
            _bffOptions,
            rawSessionToken,
            renewal.ExpiresAtUtc);

        return Ok(new AuthSessionRenewalResponse(renewal.ExpiresAtUtc));
    }

    /// <summary>
    /// Cap request token chong CSRF cho SPA; token nay khong phai credential dang nhap.
    /// </summary>
    [HttpGet("csrf")]
    [DisableRateLimiting]
    [ProducesResponseType(typeof(CsrfTokenResponse), StatusCodes.Status200OK)]
    public IActionResult GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException("Antiforgery request token was not generated.");
        }

        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";

        return Ok(new CsrfTokenResponse(
            tokens.RequestToken,
            _bffOptions.CsrfHeaderName));
    }
}
