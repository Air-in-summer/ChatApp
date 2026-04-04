using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

public class TokenCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TokenCleanupService> _logger;

    public TokenCleanupService(IServiceProvider serviceProvider, ILogger<TokenCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Vòng lặp chạy ngầm suốt dòng đời app để quét dọn thẻ rác.
    /// </summary>
    /// <param name="stoppingToken">Cờ tín hiệu hệ thống báo yêu cầu dừng app</param>
    /// <returns>Task đại diện tiến trình chạy nền</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Vòng lặp vô bờ bến đến khi bị CancellationToken gọi hàm cancel.
    /// 2. Tạo một Dependency Injection Scope mới (Vì BackgroundService là Singleton, còn AppDbContext là Scoped).
    /// 3. Lấy DbContext, tìm các Token có ExpiresAt quá hạn HOẶC bị đánh dấu IsRevoked.
    /// 4. Xóa vĩnh viễn khỏi DB, giúp bảng không bị phình to.
    /// 5. Ngủ sâu 24 giờ rồi lặp lại tác vụ.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("TokenCleanupService running...");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var expiredTokens = await dbContext.RefreshTokens
                    .Where(t => t.ExpiresAt <= DateTime.UtcNow || t.IsRevoked)
                    .ToListAsync(stoppingToken);

                if (expiredTokens.Any())
                {
                    dbContext.RefreshTokens.RemoveRange(expiredTokens);
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Cleaned up {Count} expired/revoked refresh tokens.", expiredTokens.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while cleaning up refresh tokens.");
            }

            // Run once every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
