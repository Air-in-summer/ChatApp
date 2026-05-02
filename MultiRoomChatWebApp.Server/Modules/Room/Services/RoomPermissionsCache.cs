using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Room.Services;

/// <summary>
/// Quản lý phân quyền với Redis SETs (SADD, SISMEMBER) để đạt hiệu năng O(1) kiểm tra.
/// </summary>
public class RoomPermissionsCache : IRoomPermissionsCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<RoomPermissionsCache> _logger;

    public RoomPermissionsCache(IConnectionMultiplexer redis, AppDbContext dbContext, ILogger<RoomPermissionsCache> logger)
    {
        _redis = redis;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Check quyền bằng Redis `SISMEMBER`
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Kiểm tra khóa `room_members:{roomId}` trên Redis có tồn tại không (EXISTS).
    /// 2. Nếu CÓ: Dùng lệnh `SISMEMBER` để check `userId`. Nếu trả 1 -> True, 0 -> False.
    /// 3. Nếu KHÔNG (Cache Miss):
    ///    - Query Postgres lấy mảng ID của mọi thành viên trong Room.
    ///    - Đưa mảng ID đó nạp lên Redis qua lệnh `SADD`. Set TTL (ví dụ 1 tiếng).
    ///    - Kiểm tra lại điều kiện nội bộ và trả kết quả.
    /// </remarks>
    public async Task<bool> IsUserInRoomAsync(Guid roomId, Guid userId)
    {
        var db = _redis.GetDatabase();
        string cacheKey = $"room_members:{roomId}";

        // 1. Kiểm tra xem Cache Key có đang sống hay không
        bool keyExists = await db.KeyExistsAsync(cacheKey);

        if (keyExists)
        {
            // Gia hạn thời gian sống (Sliding Expiration): Giúp các phòng đang chat sôi nổi luôn được giữ trên RAM.
            await db.KeyExpireAsync(cacheKey, TimeSpan.FromHours(1));

            // 2. Cache Hit: Kiểm tra trực tiếp trên Redis Set với O(1) delay
            return await db.SetContainsAsync(cacheKey, userId.ToString());
        }

        _logger.LogInformation("Cache MISS for room {RoomId}. Fallback to PostgreSQL.", roomId);

        // 3. Cache Miss: Đi xuống kho đĩa PostgreSQL nạp dữ liệu lên Memory
        var memberIds = await _dbContext.RoomMembers
            .AsNoTracking()
            .Where(rm => rm.RoomId == roomId)
            .Select(rm => rm.UserId.ToString())
            .ToListAsync();

        // 3.1 Nạp lên Redis
        if (memberIds.Any())
        {
            var redisValues = memberIds.Select(id => (RedisValue)id).ToArray();
            await db.SetAddAsync(cacheKey, redisValues);
            
            // Set bộ đếm tự huỷ TTL = 1 tiếng (Giảm tải RAM nếu phòng chat Dead (không ai nch))
            await db.KeyExpireAsync(cacheKey, TimeSpan.FromHours(1));

            // Trả kết quả: xem UserId có nằm trong mảng vừa lấy không
            return memberIds.Contains(userId.ToString());
        }

        // Bọn này query không ra ai trong DB -> Room ma hoặc đã xóa
        return false;
    }

    /// <summary>
    /// Xóa bộ nhớ đệm
    /// </summary>
    public async Task InvalidateRoomCacheAsync(Guid roomId)
    {
        var db = _redis.GetDatabase();
        string cacheKey = $"room_members:{roomId}";
        await db.KeyDeleteAsync(cacheKey);
    }
}
