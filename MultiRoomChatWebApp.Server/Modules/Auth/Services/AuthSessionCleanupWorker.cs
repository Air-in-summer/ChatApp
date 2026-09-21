using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Services;

/// <summary>
/// Background Service chạy ngầm chịu trách nhiệm dọn dẹp cơ sở dữ liệu.
/// Định kỳ xóa các phiên đăng nhập (BFF sessions) đã hết hạn (Expired) 
/// hoặc đã bị thu hồi (Revoked) vượt quá thời gian lưu giữ quy định,
/// nhằm tối ưu hóa không gian lưu trữ và đảm bảo hiệu suất truy vấn.
/// </summary>
public sealed class AuthSessionCleanupWorker : BackgroundService
{
    private const int DefaultScanIntervalMinutes = 60;
    private const int DefaultExpiredRetentionDays = 7;
    private const int DefaultRevokedRetentionDays = 7;
    private const int DefaultBatchSize = 500;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuthSessionCleanupWorker> _logger;
    private readonly TimeSpan _scanInterval;
    private readonly TimeSpan _expiredRetention;
    private readonly TimeSpan _revokedRetention;
    private readonly int _batchSize;

    public AuthSessionCleanupWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<AuthSessionCleanupWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _scanInterval = TimeSpan.FromMinutes(Math.Max(
            1,
            configuration.GetValue<int?>("Auth:SessionCleanup:ScanIntervalMinutes")
                ?? DefaultScanIntervalMinutes));
        _expiredRetention = TimeSpan.FromDays(Math.Max(
            0,
            configuration.GetValue<int?>("Auth:SessionCleanup:ExpiredRetentionDays")
                ?? DefaultExpiredRetentionDays));
        _revokedRetention = TimeSpan.FromDays(Math.Max(
            0,
            configuration.GetValue<int?>("Auth:SessionCleanup:RevokedRetentionDays")
                ?? DefaultRevokedRetentionDays));
        _batchSize = Math.Clamp(
            configuration.GetValue<int?>("Auth:SessionCleanup:BatchSize")
                ?? DefaultBatchSize,
            1,
            5000);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AuthSessionCleanupWorker started. ScanInterval={ScanIntervalMinutes}m; ExpiredRetention={ExpiredRetentionDays}d; RevokedRetention={RevokedRetentionDays}d; BatchSize={BatchSize}",
            _scanInterval.TotalMinutes,
            _expiredRetention.TotalDays,
            _revokedRetention.TotalDays,
            _batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while cleaning up auth sessions.");
            }

            try
            {
                await Task.Delay(_scanInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunCleanupBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var expiredCutoff = now.Subtract(_expiredRetention);
        var revokedCutoff = now.Subtract(_revokedRetention);

        var removedCount = await dbContext.AuthSessions
            .Where(session =>
                session.ExpiresAt <= expiredCutoff ||
                (session.RevokedAt != null && session.RevokedAt <= revokedCutoff))
            .OrderBy(session => session.ExpiresAt)
            .Take(_batchSize)
            .ExecuteDeleteAsync(cancellationToken);

        if (removedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} auth sessions.", removedCount);
        }
    }
}
