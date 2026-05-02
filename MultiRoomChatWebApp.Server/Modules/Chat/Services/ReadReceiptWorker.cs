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

        using var timer = new PeriodicTimer(FlushInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FlushReadReceiptsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong ReadReceipts Batching Worker. Sẽ thử lại sau 30s.");
            }
        }
    }

    /// <summary>
    /// Thực hiện một lần Flush: Quét Redis bằng SCAN và xử lý cuốn chiếu (Streaming) để bảo vệ RAM.
    /// </summary>
    private async Task FlushReadReceiptsAsync(CancellationToken stoppingToken)
    {
        var redisDb = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);

        var receiptsBuffer = new List<ReadReceipt>();
        var redisKeysBuffer = new List<RedisKey>();

        // Giới hạn Buffer để Flush xuống DB (Tránh OOM RAM)
        const int flushThreshold = 1000; 

        // 1. Quét Redis cuốn chiếu
        await foreach (var key in server.KeysAsync(pattern: "Room:*:ReadReceipts"))
        {
            var parts = ((string)key!).Split(':');
            if (parts.Length < 3 || !Guid.TryParse(parts[1], out var roomId)) continue;

            var hashEntries = await redisDb.HashGetAllAsync(key);
            if (hashEntries.Length == 0) continue;

            foreach (var entry in hashEntries)
            {
                if (!Guid.TryParse(entry.Name, out var userId)) continue;

                receiptsBuffer.Add(new ReadReceipt
                {
                    UserId = userId,
                    RoomId = roomId,
                    LastReadMessageId = entry.Value.ToString(),
                    UpdatedAt = DateTime.UtcNow
                });
            }

            redisKeysBuffer.Add(key);

            // [BƯỚC QUAN TRỌNG]: Nếu buffer đủ lớn, thực hiện Flush ngay để giải phóng RAM
            if (receiptsBuffer.Count >= flushThreshold)
            {
                await ProcessBufferAsync(receiptsBuffer, redisKeysBuffer, stoppingToken);
                // Sau khi Flush xong, Buffer sẽ trống để nhận đợt tiếp theo
            }
        }

        // 2. Flush nốt những bản ghi cuối cùng còn sót lại trong buffer
        if (receiptsBuffer.Any())
        {
            await ProcessBufferAsync(receiptsBuffer, redisKeysBuffer, stoppingToken);
        }
    }

    /// <summary>
    /// Lưu dữ liệu xuống DB và xóa key Redis cho một cụm dữ liệu (Batch).
    /// </summary>
    private async Task ProcessBufferAsync(List<ReadReceipt> receipts, List<RedisKey> redisKeys, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redisDb = _redis.GetDatabase();

        _logger.LogInformation("🚀 ReadReceiptWorker: Đang Flush {Count} bản ghi cuốn chiếu...", receipts.Count);

        // Bước 1: Upsert vào DB (Dùng lại hàm ExecuteUpsertBatchAsync đã viết)
        // Lưu ý: Nếu batch này lớn hơn 500, ta vẫn chia nhỏ tiếp để an toàn cho SQL Parameters
        const int sqlBatchSize = 500;
        for (int i = 0; i < receipts.Count; i += sqlBatchSize)
        {
            var batch = receipts.Skip(i).Take(sqlBatchSize).ToList();
            await ExecuteUpsertBatchAsync(dbContext, batch, ct);
        }

        // Bước 2: Xóa key Redis tương ứng
        foreach (var key in redisKeys)
        {
            await redisDb.KeyDeleteAsync(key);
        }

        // Bước 3: Dọn dẹp buffer để nhận đợt mới
        receipts.Clear();
        redisKeys.Clear();
    }

    /// <summary>
    /// Thực thi lệnh SQL UPSERT (INSERT ... ON CONFLICT DO UPDATE) cho một Batch.
    /// </summary>
    private async Task ExecuteUpsertBatchAsync(AppDbContext dbContext, List<ReadReceipt> batch, CancellationToken ct)
    {
        var sql = new System.Text.StringBuilder();
        sql.Append("INSERT INTO \"ReadReceipts\" (\"UserId\", \"RoomId\", \"LastReadMessageId\", \"UpdatedAt\") VALUES ");
        
        var parameters = new List<object>();
        for (int j = 0; j < batch.Count; j++)
        {
            var r = batch[j];
            int pIdx = j * 4;
            sql.Append($"(@p{pIdx}, @p{pIdx + 1}, @p{pIdx + 2}, @p{pIdx + 3})");
            
            if (j < batch.Count - 1) sql.Append(", ");
            
            parameters.Add(r.UserId);
            parameters.Add(r.RoomId);
            parameters.Add(r.LastReadMessageId);
            parameters.Add(r.UpdatedAt);
        }
        
        sql.Append(" ON CONFLICT (\"UserId\", \"RoomId\") DO UPDATE SET ");
        sql.Append("\"LastReadMessageId\" = EXCLUDED.\"LastReadMessageId\", ");
        sql.Append("\"UpdatedAt\" = EXCLUDED.\"UpdatedAt\";");

        await dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
    }
}
