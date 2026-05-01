using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Background Worker chạy ngầm mỗi 30 giây.
/// Quét toàn bộ dữ liệu "Đã xem" (ReadReceipts) trên Redis và Flush xuống PostgreSQL.
///
/// Chiến lược Upsert: "Xóa trước - Thêm sau" (Bulk Delete + Bulk Insert).
/// - Ưu điểm: Nhanh, không tốn N+1 SELECT để kiểm tra từng dòng.
/// - Không lo mất mát AutoIncrement ID vì bảng ReadReceipts dùng Composite PK (UserId, RoomId).
/// </summary>
public class ReadReceiptWorker : BackgroundService
{
    private readonly ILogger<ReadReceiptWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;

    // Chu kỳ Flush từ Redis → PostgreSQL
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    public ReadReceiptWorker(
        ILogger<ReadReceiptWorker> logger,
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _redis = redis;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 ReadReceiptWorker đã khởi động. Chu kỳ Flush: {Interval}s.", FlushInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, stoppingToken);
                await FlushReadReceiptsAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // App đang tắt, thoát vòng lặp an toàn
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong ReadReceipts Batching Worker. Sẽ thử lại sau 30s.");
            }
        }
    }

    /// <summary>
    /// Thực hiện một lần Flush: Quét Redis → Upsert bulk (Xóa cũ + Thêm mới) → Xóa key Redis.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. SCAN tất cả key khớp "Room:*:ReadReceipts" trong Redis (an toàn hơn KEYS *).
    /// 2. Với mỗi key, parse RoomId và lấy toàn bộ Hash (userId → lastReadMessageId).
    /// 3. Xóa sạch các bản ghi cũ theo (UserId, RoomId) bằng ExecuteDeleteAsync (Bulk, không qua Change Tracker).
    /// 4. Insert toàn bộ bản ghi mới bằng AddRangeAsync (1 batch duy nhất).
    /// 5. Xóa key Redis sau khi Flush thành công để giải phóng RAM.
    ///
    /// Lưu ý:
    /// - Không dùng First/Update riêng từng dòng (N+1 SELECT) → Không hiệu quả khi nhiều phòng.
    /// - Key Redis chỉ bị xóa SAU KHI SaveChangesAsync thành công → Không mất data nếu DB lỗi.
    /// </remarks>
    private async Task FlushReadReceiptsAsync(CancellationToken stoppingToken)
    {
        var redisDb = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);

        // 1. Scan tất cả key theo pattern
        var keys = server.Keys(pattern: "Room:*:ReadReceipts").ToList();
        if (keys.Count == 0) return;

        _logger.LogInformation("📦 ReadReceiptWorker: Tìm thấy {Count} key cần xử lý.", keys.Count);

        var receiptsToInsert = new List<ReadReceipt>();
        var receiptKeys = new List<(Guid UserId, Guid RoomId)>();
        var processedRedisKeys = new List<RedisKey>();

        // 2. Với mỗi key, giải mã RoomId và lấy toàn bộ Hash
        foreach (var key in keys)
        {
            // Key format: "Room:{roomId}:ReadReceipts"
            var parts = ((string)key!).Split(':');
            if (parts.Length < 3 || !Guid.TryParse(parts[1], out var roomId)) continue;

            var hashEntries = await redisDb.HashGetAllAsync(key);
            if (hashEntries.Length == 0) continue;

            foreach (var entry in hashEntries)
            {
                if (!Guid.TryParse(entry.Name, out var userId)) continue;

                receiptsToInsert.Add(new ReadReceipt
                {
                    UserId = userId,
                    RoomId = roomId,
                    LastReadMessageId = entry.Value.ToString(),
                    UpdatedAt = DateTime.UtcNow
                });

                // Ghi nhớ cặp (UserId, RoomId) để xóa bản ghi cũ
                receiptKeys.Add((userId, roomId));
            }

            processedRedisKeys.Add(key);
        }

        if (receiptsToInsert.Count == 0) return;

        // 3. Upsert bulk trong 1 scope DB mới
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Bước 3a: Xóa sạch các bản ghi cũ theo từng cặp (UserId, RoomId)
        // Dùng ExecuteDeleteAsync để tránh load Entity vào RAM (không qua Change Tracker)
        foreach (var (userId, roomId) in receiptKeys)
        {
            await dbContext.ReadReceipts
                .Where(r => r.UserId == userId && r.RoomId == roomId)
                .ExecuteDeleteAsync(stoppingToken);
        }

        // Bước 3b: Insert tất cả bản ghi mới trong 1 batch duy nhất
        await dbContext.ReadReceipts.AddRangeAsync(receiptsToInsert, stoppingToken);
        await dbContext.SaveChangesAsync(stoppingToken);

        // 4. Chỉ xóa key Redis SAU KHI lưu DB thành công
        foreach (var redisKey in processedRedisKeys)
        {
            await redisDb.KeyDeleteAsync(redisKey);
        }

        _logger.LogInformation(
            "🧹 ReadReceiptWorker: Đã Flush thành công {Count} ReadReceipt xuống PostgreSQL và xóa {KeyCount} key Redis.",
            receiptsToInsert.Count, processedRedisKeys.Count);
    }
}
