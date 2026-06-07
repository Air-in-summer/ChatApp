using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class ReadReceiptWorker : BackgroundService
{
    private const string DeletePersistedReceiptScript = """
        local currentMessageId = redis.call('HGET', KEYS[1], ARGV[1])
        if currentMessageId ~= ARGV[2] then
            return 0
        end

        redis.call('HDEL', KEYS[1], ARGV[1])
        return 1
        """;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger<ReadReceiptWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;

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
        using var timer = new PeriodicTimer(FlushInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FlushReadReceiptsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Read receipt flush that bai. Thu lai sau 30 giay.");
            }
        }
    }

    private async Task FlushReadReceiptsAsync(CancellationToken cancellationToken)
    {
        var redisDb = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var buffer = new List<ReadReceipt>();

        await foreach (var key in server.KeysAsync(pattern: "Room:*:ReadReceipts"))
        {
            var parts = key.ToString().Split(':');
            if (parts.Length < 3 || !Guid.TryParse(parts[1], out var roomId))
            {
                continue;
            }

            var entries = await redisDb.HashGetAllAsync(key);
            foreach (var entry in entries)
            {
                if (!Guid.TryParse(entry.Name.ToString(), out var userId))
                {
                    continue;
                }

                buffer.Add(new ReadReceipt
                {
                    UserId = userId,
                    RoomId = roomId,
                    LastReadMessageId = entry.Value.ToString(),
                    UpdatedAt = DateTime.UtcNow
                });
            }

            if (buffer.Count >= 1_000)
            {
                await ProcessBufferAsync(buffer, cancellationToken);
            }
        }

        if (buffer.Count > 0)
        {
            await ProcessBufferAsync(buffer, cancellationToken);
        }
    }

    private async Task ProcessBufferAsync(
        List<ReadReceipt> receipts,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        const int batchSize = 500;
        for (var index = 0; index < receipts.Count; index += batchSize)
        {
            var batch = receipts.Skip(index).Take(batchSize).ToList();
            await ExecuteUpsertBatchAsync(dbContext, batch, cancellationToken);
        }

        var redisDb = _redis.GetDatabase();
        await Task.WhenAll(receipts.Select(receipt =>
            redisDb.ScriptEvaluateAsync(
                DeletePersistedReceiptScript,
                [(RedisKey)$"Room:{receipt.RoomId}:ReadReceipts"],
                [receipt.UserId.ToString(), receipt.LastReadMessageId])));

        receipts.Clear();
    }

    private static async Task ExecuteUpsertBatchAsync(
        AppDbContext dbContext,
        IReadOnlyList<ReadReceipt> batch,
        CancellationToken cancellationToken)
    {
        var sql = new System.Text.StringBuilder();
        sql.Append(
            "INSERT INTO \"ReadReceipts\" " +
            "(\"UserId\", \"RoomId\", \"LastReadMessageId\", \"UpdatedAt\") VALUES ");

        var parameters = new List<object>();
        for (var index = 0; index < batch.Count; index++)
        {
            var receipt = batch[index];
            var parameterIndex = index * 4;
            sql.Append(
                $"(@p{parameterIndex}, @p{parameterIndex + 1}, " +
                $"@p{parameterIndex + 2}, @p{parameterIndex + 3})");

            if (index < batch.Count - 1)
            {
                sql.Append(", ");
            }

            parameters.Add(receipt.UserId);
            parameters.Add(receipt.RoomId);
            parameters.Add(receipt.LastReadMessageId);
            parameters.Add(receipt.UpdatedAt);
        }

        sql.Append(
            " ON CONFLICT (\"UserId\", \"RoomId\") DO UPDATE SET " +
            "\"LastReadMessageId\" = EXCLUDED.\"LastReadMessageId\", " +
            "\"UpdatedAt\" = EXCLUDED.\"UpdatedAt\" " +
            "WHERE EXCLUDED.\"LastReadMessageId\" > " +
            "\"ReadReceipts\".\"LastReadMessageId\";");

        await dbContext.Database.ExecuteSqlRawAsync(
            sql.ToString(),
            parameters,
            cancellationToken);
    }
}
