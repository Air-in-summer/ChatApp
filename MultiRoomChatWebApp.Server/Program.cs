using System.Threading.RateLimiting;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Middleware;
using Serilog;
using System.Security.Claims;

// ──────────────────────────────────────────────────────────
// 1. SERILOG: Bootstrap logger (catches startup errors)
// ──────────────────────────────────────────────────────────
const string GoogleExternalCookieScheme = "GoogleExternal";

static string NormalizeInternalReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
        return "/";

    var trimmed = returnUrl.Trim();
    if (!trimmed.StartsWith('/') || trimmed.StartsWith("//") || trimmed.Contains('\\'))
        return "/";

    return trimmed;
}

static string GetUserRateLimitPartitionKey(HttpContext httpContext)
{
    return httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
}

static string BuildFrontendOAuthCallbackUrl(IConfiguration configuration, string oauthError, string returnUrl)
{
    var frontendCallbackUrl = configuration["Authentication:Google:FrontendCallbackUrl"];
    if (string.IsNullOrWhiteSpace(frontendCallbackUrl))
        frontendCallbackUrl = "/oauth/callback";

    var separator = frontendCallbackUrl.Contains('?') ? '&' : '?';
    return $"{frontendCallbackUrl}{separator}oauthError={Uri.EscapeDataString(oauthError)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("Logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

try
{
    Log.Information("Starting ChatApp server...");

    var builder = WebApplication.CreateBuilder(args);

    // Replace default logger with Serilog
    builder.Host.UseSerilog();

    // ──────────────────────────────────────────────────────────
    // 2. SERVICES REGISTRATION
    // ──────────────────────────────────────────────────────────

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Serialize Enum thành chuỗi thay vì số nguyên
            // Ví dụ: RoomType.DirectMessage → "DirectMessage" (thay vì 2)
            // Frontend cần chuỗi để filter/so sánh chính xác
            options.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    
    // Register Entity Framework Core DbContext
    builder.Services.AddDbContext<MultiRoomChatWebApp.Server.Infrastructure.Database.AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

    // Config MongoDB Conventions (camelCase & Enum as String)
    var pack = new MongoDB.Bson.Serialization.Conventions.ConventionPack
    {
        new MongoDB.Bson.Serialization.Conventions.CamelCaseElementNameConvention(),
        new MongoDB.Bson.Serialization.Conventions.EnumRepresentationConvention(MongoDB.Bson.BsonType.String)
    };
    MongoDB.Bson.Serialization.Conventions.ConventionRegistry.Register("MongoConventions", pack, t => true);

    // Register MongoDB
    var mongoClient = new MongoDB.Driver.MongoClient(builder.Configuration.GetConnectionString("MongoDB"));
    builder.Services.AddSingleton<MongoDB.Driver.IMongoClient>(mongoClient);
    builder.Services.AddScoped<MongoDB.Driver.IMongoDatabase>(sp => 
        sp.GetRequiredService<MongoDB.Driver.IMongoClient>().GetDatabase("ChatAppDB_Mongo"));

    // Configure Redis Distributed Cache & Multiplexer
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Missing Redis config");

    // Đăng ký DistributedCache (chuẩn .NET)
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "ChatApp_";
    });

    // Đăng ký ConnectionMultiplexer (để xài lệnh thuần SADD, SISMEMBER)
    var redisMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString);
    
    builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(redisMultiplexer);

    // Register Media Storage foundation (S3-compatible; local/dev dùng MinIO trên D:\ChatAppData\minio)
    builder.Services.Configure<MediaStorageOptions>(
        builder.Configuration.GetSection(MediaStorageOptions.SectionName));
    builder.Services.AddSingleton<IAmazonS3>(sp =>
    {
        var mediaOptions = sp.GetRequiredService<IOptions<MediaStorageOptions>>().Value;
        AWSCredentials credentials = string.IsNullOrWhiteSpace(mediaOptions.AccessKey) ||
                                     string.IsNullOrWhiteSpace(mediaOptions.SecretKey)
            ? new AnonymousAWSCredentials()
            : new BasicAWSCredentials(mediaOptions.AccessKey, mediaOptions.SecretKey);

        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = mediaOptions.ForcePathStyle,
            RegionEndpoint = RegionEndpoint.USEast1
        };

        if (!string.IsNullOrWhiteSpace(mediaOptions.Endpoint))
        {
            s3Config.ServiceURL = mediaOptions.Endpoint;
            s3Config.UseHttp = mediaOptions.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        return new AmazonS3Client(credentials, s3Config);
    });
    builder.Services.AddSingleton<MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces.IMediaStorageService, MultiRoomChatWebApp.Server.Modules.Media.Services.S3MediaStorageService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces.IMediaService, MultiRoomChatWebApp.Server.Modules.Media.Services.MediaService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces.IMediaValidationService, MultiRoomChatWebApp.Server.Modules.Media.Services.MediaValidationService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces.IAvatarMediaService, MultiRoomChatWebApp.Server.Modules.Media.Services.AvatarMediaService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces.IChatMediaService, MultiRoomChatWebApp.Server.Modules.Media.Services.ChatMediaService>();
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Media.Services.MediaStorageBootstrapService>();
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Media.Services.PendingMediaCleanupWorker>();

    // Register Auth Services
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces.IJwtService, MultiRoomChatWebApp.Server.Modules.Auth.Services.JwtService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces.IAuthService, MultiRoomChatWebApp.Server.Modules.Auth.Services.AuthService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces.IUserCacheService, MultiRoomChatWebApp.Server.Modules.User.Services.UserCacheService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces.IUserService, MultiRoomChatWebApp.Server.Modules.User.Services.UserService>();
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Auth.Services.TokenCleanupService>();

    // Register MediatR
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

    // Register Room Services
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces.IRoomService, MultiRoomChatWebApp.Server.Modules.Room.Services.RoomService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces.IRoomPermissionsCache, MultiRoomChatWebApp.Server.Modules.Room.Services.RoomPermissionsCache>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces.IRoomMetadataCache, MultiRoomChatWebApp.Server.Modules.Room.Services.RoomMetadataCache>();

    // Register Group Services
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces.IGroupService, MultiRoomChatWebApp.Server.Modules.Group.Services.GroupService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces.IGroupPermissionsCache, MultiRoomChatWebApp.Server.Modules.Group.Services.GroupPermissionsCache>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces.IGroupMetadataCache, MultiRoomChatWebApp.Server.Modules.Group.Services.GroupMetadataCache>();

    // Register User Relationship Services
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces.IUserRelationshipService, MultiRoomChatWebApp.Server.Modules.User.Services.UserRelationshipService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces.IUserRelationshipGraphService, MultiRoomChatWebApp.Server.Modules.User.Services.UserRelationshipGraphService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces.IUserPresenceService, MultiRoomChatWebApp.Server.Modules.User.Services.UserPresenceService>();

    // Register Chat / SignalR Services
    builder.Services.AddSignalR()
        .AddJsonProtocol(options => {
            options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces.IChatService, MultiRoomChatWebApp.Server.Modules.Chat.Services.ChatService>();
    // Đăng ký Worker nhồi dữ liệu từ Redis vào MongoDB chạy ngầm vô thời hạn
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Chat.Services.MessagePersistenceWorker>();
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Chat.Services.ReadReceiptWorker>();
    // Tracker đếm số lượng người online/offline (Dùng Singleton để chia sẻ bộ nhớ cho toàn HTTP pipeline)
    builder.Services.AddSingleton<MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces.IPresenceTracker, MultiRoomChatWebApp.Server.Modules.Chat.Services.RedisPresenceTracker>();
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Chat.Services.PresenceCleanupWorker>();

    // Register Voice Services (LiveKit Token)
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces.IVoiceTokenService, MultiRoomChatWebApp.Server.Modules.Voice.Services.VoiceTokenService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces.IVoiceSessionService, MultiRoomChatWebApp.Server.Modules.Voice.Services.VoiceSessionService>();
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Voice.Services.VoiceMissedCallWorker>();

    // Configure Authentication
    // JWT Bearer vẫn là scheme mặc định cho API/SignalR. Google chỉ là external scheme
    // được gọi rõ bằng Challenge ở endpoint OAuth, không thay thế JWT nội bộ của app.
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddCookie(GoogleExternalCookieScheme, options =>
        {
            options.Cookie.Name = "googleExternalAuth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/api/auth/google";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            options.SlidingExpiration = false;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
            };

            // Hook Event để Bắt Token từ SignalR Websocket Query String (?access_token=...)
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    
                    // Nếu là đường dẫn của Hub thì mình mới bắt token kiểu ảo này
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub/chat"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        })
        .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
        {
            var googleSection = builder.Configuration.GetSection("Authentication:Google");

            options.SignInScheme = GoogleExternalCookieScheme;
            options.ClientId = googleSection["ClientId"] ?? string.Empty;
            options.ClientSecret = googleSection["ClientSecret"] ?? string.Empty;
            options.CallbackPath = googleSection["CallbackPath"] ?? "/api/auth/google/callback";
            options.SaveTokens = false;

            // Chỉ xin scope tối thiểu: identity, email và profile. Frontend không nhận Google token.
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("email");
            options.Scope.Add("profile");

            options.Events.OnCreatingTicket = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                    return Task.CompletedTask;

                if (context.User.TryGetProperty("email_verified", out var emailVerified))
                {
                    var isVerified = emailVerified.ValueKind == System.Text.Json.JsonValueKind.True
                        || string.Equals(emailVerified.GetString(), "true", StringComparison.OrdinalIgnoreCase);
                    identity.AddClaim(new Claim("email_verified", isVerified.ToString().ToLowerInvariant()));
                }

                if (context.User.TryGetProperty("picture", out var picture))
                {
                    var pictureUrl = picture.GetString();
                    if (!string.IsNullOrWhiteSpace(pictureUrl))
                        identity.AddClaim(new Claim("picture", pictureUrl));
                }

                return Task.CompletedTask;
            };

            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();

                var remoteError = context.Request.Query["error"].ToString();
                var errorCode = string.Equals(remoteError, "access_denied", StringComparison.OrdinalIgnoreCase)
                    ? "access_denied"
                    : "oauth_failed";

                var returnUrl = context.Properties?.Items.TryGetValue("returnUrl", out var storedReturnUrl) == true
                    ? storedReturnUrl
                    : null;
                var safeReturnUrl = NormalizeInternalReturnUrl(returnUrl);

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleOAuth");
                logger.LogWarning(
                    "Đăng nhập OAuth thất bại tại callback từ provider. Provider={Provider}; Reason={Reason}",
                    GoogleDefaults.AuthenticationScheme,
                    errorCode);

                context.Response.Redirect(BuildFrontendOAuthCallbackUrl(
                    builder.Configuration,
                    errorCode,
                    safeReturnUrl));

                return Task.CompletedTask;
            };
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "MultiRoomChat API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Dán duy nhất chuỗi JWT token vào đây (Không cần chữ 'Bearer')."
        });

        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                new List<string>()
            }
        });
    });

    // FluentValidation: auto-discover all validators in this assembly
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "Dữ liệu gửi lên không hợp lệ."
                    : error.ErrorMessage)
                .Distinct()
                .ToArray();

            var message = errors.Length > 0
                ? errors[0]
                : "Dữ liệu gửi lên không hợp lệ.";

            return new BadRequestObjectResult(new
            {
                type = "about:blank",
                title = "Dữ liệu gửi lên không hợp lệ.",
                status = StatusCodes.Status400BadRequest,
                code = "validation_failed",
                message,
                detail = message,
                errors,
                traceId = context.HttpContext.TraceIdentifier
            });
        };
    });

    // CORS: only allow React dev server in Development
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>();

            if (allowedOrigins == null || allowedOrigins.Length == 0)
            {
                allowedOrigins = new[]
                {
                    "https://localhost:5173",
                    "http://localhost:5173"
                };
            }

            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Rate Limiting: protect auth endpoints from brute-force
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddFixedWindowLimiter("AuthLimit", limiter =>
        {
            limiter.PermitLimit = 5;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });

        options.AddPolicy("FriendRequestLimit", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                GetUserRateLimitPartitionKey(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));

        options.AddPolicy("BlockActionLimit", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                GetUserRateLimitPartitionKey(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    });

    // ──────────────────────────────────────────────────────────
    // 3. BUILD APP & CONFIGURE MIDDLEWARE PIPELINE
    // ──────────────────────────────────────────────────────────
    var app = builder.Build();

    // Serilog đứng ngoài ErrorHandlingMiddleware để log status code cuối cùng sau khi lỗi được map.
    app.UseSerilogRequestLogging();

    // Global Error Handler
    app.UseMiddleware<ErrorHandlingMiddleware>();

    // Security Headers (X-Frame-Options, CSP, etc.)
    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (app.Environment.IsDevelopment())
    {
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/api/v1/voice/livekit/webhook"),
            branch => branch.UseHttpsRedirection());
    }
    else
    {
        app.UseHttpsRedirection();
    }

    app.UseCors("AllowFrontend");
    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<MultiRoomChatWebApp.Server.Modules.Chat.Hubs.ChatHub>("/hub/chat");

    app.MapFallbackToFile("/index.html");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
