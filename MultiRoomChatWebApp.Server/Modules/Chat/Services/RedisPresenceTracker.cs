using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Redis-backed presence tracker theo mô hình một hash key mỗi user, mỗi connection là một field có hạn logic.
/// </summary>
public class RedisPresenceTracker : IPresenceTracker
{
    private const string ActiveUsersKey = "presence:active_users";
    private static readonly TimeSpan ConnectionTtl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan UserKeyTtl = TimeSpan.FromSeconds(180);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPresenceTracker> _logger;

    public RedisPresenceTracker(
        IConnectionMultiplexer redis,
        ILogger<RedisPresenceTracker> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Thêm connection mới vào hash của user và trả true nếu trước đó user đang offline.
    /// </summary>
    public async Task<bool> UserConnected(Guid userId, string connectionId)
    {
        var now = DateTimeOffset.UtcNow;
        var wasOnline = await HasValidConnectionAsync(userId, now, pruneExpired: true);

        await SetConnectionExpiryAsync(userId, connectionId, now);

        return !wasOnline;
    }

    /// <summary>
    /// Xóa connection khỏi hash của user và trả true nếu user không còn connection hợp lệ.
    /// </summary>
    public async Task<bool> UserDisconnected(Guid userId, string connectionId)
    {
        var db = _redis.GetDatabase();
        var key = GetUserConnectionsKey(userId);

        await db.HashDeleteAsync(key, connectionId);

        var isStillOnline = await HasValidConnectionAsync(userId, DateTimeOffset.UtcNow, pruneExpired: true);
        if (isStillOnline)
        {
            return false;
        }

        await db.SetRemoveAsync(ActiveUsersKey, userId.ToString());
        return true;
    }

    /// <summary>
    /// Gia hạn connection bằng cách cập nhật expiresAt. Nếu connection/user đã bị cleanup thì đưa user online lại.
    /// </summary>
    public async Task<bool> TouchHeartbeatAsync(Guid userId, string connectionId)
    {
        var now = DateTimeOffset.UtcNow;
        var wasOnline = await HasValidConnectionAsync(userId, now, pruneExpired: true);

        await SetConnectionExpiryAsync(userId, connectionId, now);

        return !wasOnline;
    }

    /// <summary>
    /// Kiểm tra user có connection hợp lệ sau khi prune field hết hạn.
    /// </summary>
    public async Task<bool> IsOnlineAsync(Guid userId)
    {
        return await HasValidConnectionAsync(userId, DateTimeOffset.UtcNow, pruneExpired: true);
    }

    /// <summary>
    /// Lấy subset online của danh sách userId. Mỗi user được prune opportunistic để tránh treo online.
    /// </summary>
    public async Task<IEnumerable<Guid>> GetOnlineUsersAsync(IEnumerable<Guid> userIds)
    {
        var onlineUserIds = new List<Guid>();

        foreach (var userId in userIds.Distinct())
        {
            if (await IsOnlineAsync(userId))
            {
                onlineUserIds.Add(userId);
            }
        }

        return onlineUserIds;
    }

    /// <summary>
    /// Quét tập user active/recent, prune connection hết hạn và trả về user vừa mất connection cuối cùng.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> CleanupExpiredConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var activeUsers = await db.SetMembersAsync(ActiveUsersKey);
        if (activeUsers.Length == 0)
        {
            return Array.Empty<Guid>();
        }

        var offlineUserIds = new List<Guid>();
        var now = DateTimeOffset.UtcNow;

        foreach (var activeUser in activeUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Guid.TryParse(activeUser.ToString(), out var userId))
            {
                await db.SetRemoveAsync(ActiveUsersKey, activeUser);
                continue;
            }

            var isOnline = await HasValidConnectionAsync(userId, now, pruneExpired: true);
            if (isOnline)
            {
                continue;
            }

            await db.SetRemoveAsync(ActiveUsersKey, activeUser);
            offlineUserIds.Add(userId);
        }

        if (offlineUserIds.Count > 0)
        {
            _logger.LogInformation(
                "Presence cleanup marked {OfflineCount} users offline after TTL expiry.",
                offlineUserIds.Count);
        }

        return offlineUserIds;
    }

    private async Task SetConnectionExpiryAsync(Guid userId, string connectionId, DateTimeOffset now)
    {
        var db = _redis.GetDatabase();
        var key = GetUserConnectionsKey(userId);
        var expiresAtUnixMs = now.Add(ConnectionTtl).ToUnixTimeMilliseconds();

        await db.HashSetAsync(key, connectionId, expiresAtUnixMs);
        await db.KeyExpireAsync(key, UserKeyTtl);
        await db.SetAddAsync(ActiveUsersKey, userId.ToString());
    }

    private async Task<bool> HasValidConnectionAsync(
        Guid userId,
        DateTimeOffset now,
        bool pruneExpired)
    {
        var db = _redis.GetDatabase();
        var key = GetUserConnectionsKey(userId);
        var entries = await db.HashGetAllAsync(key);

        if (entries.Length == 0)
        {
            return false;
        }

        var nowUnixMs = now.ToUnixTimeMilliseconds();
        var expiredFields = new List<RedisValue>();
        var hasValidConnection = false;

        foreach (var entry in entries)
        {
            if (!long.TryParse(entry.Value.ToString(), out var expiresAtUnixMs) ||
                expiresAtUnixMs <= nowUnixMs)
            {
                expiredFields.Add(entry.Name);
                continue;
            }

            hasValidConnection = true;
        }

        if (pruneExpired && expiredFields.Count > 0)
        {
            await db.HashDeleteAsync(key, expiredFields.ToArray());
        }

        return hasValidConnection;
    }

    private static string GetUserConnectionsKey(Guid userId)
    {
        return $"presence:user:{userId}:connections";
    }
}
