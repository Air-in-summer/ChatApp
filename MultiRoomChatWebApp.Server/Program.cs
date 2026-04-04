using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Shared.Middleware;
using Serilog;

// ──────────────────────────────────────────────────────────
// 1. SERILOG: Bootstrap logger (catches startup errors)
// ──────────────────────────────────────────────────────────
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

    builder.Services.AddControllers();
    
    // Register Entity Framework Core DbContext
    builder.Services.AddDbContext<MultiRoomChatWebApp.Server.Infrastructure.Database.AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

    // Register MongoDB
    var mongoClient = new MongoDB.Driver.MongoClient(builder.Configuration.GetConnectionString("MongoDB"));
    builder.Services.AddSingleton<MongoDB.Driver.IMongoClient>(mongoClient);
    builder.Services.AddScoped<MongoDB.Driver.IMongoDatabase>(sp => 
        sp.GetRequiredService<MongoDB.Driver.IMongoClient>().GetDatabase("ChatAppDB_Mongo"));

    // Configure Redis Distributed Cache
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis");
        options.InstanceName = "ChatApp_";
    });

    // Register Auth Services
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces.IJwtService, MultiRoomChatWebApp.Server.Modules.Auth.Services.JwtService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces.IAuthService, MultiRoomChatWebApp.Server.Modules.Auth.Services.AuthService>();
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces.IUserCacheService, MultiRoomChatWebApp.Server.Modules.User.Services.UserCacheService>();
    builder.Services.AddHostedService<MultiRoomChatWebApp.Server.Modules.Auth.Services.TokenCleanupService>();

    // Register Room Services
    builder.Services.AddScoped<MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces.IRoomService, MultiRoomChatWebApp.Server.Modules.Room.Services.RoomService>();

    // Configure JWT Authentication
    builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
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
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
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

    // CORS: only allow React dev server in Development
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins("https://localhost:5173", "http://localhost:5173")
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
    });

    // ──────────────────────────────────────────────────────────
    // 3. BUILD APP & CONFIGURE MIDDLEWARE PIPELINE
    // ──────────────────────────────────────────────────────────
    var app = builder.Build();

    // Global Error Handler (must be first to catch everything)
    app.UseMiddleware<ErrorHandlingMiddleware>();

    // Security Headers (X-Frame-Options, CSP, etc.)
    app.UseMiddleware<SecurityHeadersMiddleware>();

    // Serilog request logging (method, path, status code, duration)
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseHttpsRedirection();
    app.UseCors("AllowFrontend");
    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
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
