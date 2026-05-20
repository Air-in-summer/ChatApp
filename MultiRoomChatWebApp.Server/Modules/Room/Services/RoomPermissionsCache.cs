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
    /// Xóa toàn bộ Cache của một phòng, bắt hệ thống phải query lại từ SQL trong lần kế tiếp.
    /// </summary>
    public async Task InvalidateRoomCacheAsync(Guid roomId)
    {
        var db = _redis.GetDatabase();
        string cacheKey = $"room_members:{roomId}";
        await db.KeyDeleteAsync(cacheKey);
    }

    /// <summary>
    /// Xóa một người dùng cụ thể khỏi Cache của phòng (SREM)
    /// </summary>
    public async Task RemoveUserFromRoomAsync(Guid roomId, Guid userId)
    {
        var db = _redis.GetDatabase();
        string cacheKey = $"room_members:{roomId}";
        
        // Chỉ thực hiện xóa nếu key đang tồn tại (tránh tạo key trống không cần thiết)
        if (await db.KeyExistsAsync(cacheKey))
        {
            await db.SetRemoveAsync(cacheKey, userId.ToString());
            _logger.LogInformation("Removed user {UserId} from room cache {RoomId}", userId, roomId);
        }
    }

    /// <summary>
    /// Thêm danh sách người dùng vào Cache của phòng (SADD batch)
    /// </summary>
    public async Task AddUsersToRoomCacheAsync(Guid roomId, IEnumerable<Guid> userIds)
    {
        if (userIds == null || !userIds.Any()) return;

        var db = _redis.GetDatabase();
        string cacheKey = $"room_members:{roomId}";

        // QUAN TRỌNG: Chỉ thực hiện nếu Cache Key đang tồn tại để tránh nạp dữ liệu thiếu (Stale Data)
        if (await db.KeyExistsAsync(cacheKey))
        {
            var redisValues = userIds.Select(id => (RedisValue)id.ToString()).ToArray();
            
            // SADD hỗ trợ mảng giá trị (Variadic) -> chỉ tốn 1 Round-trip tới Redis
            await db.SetAddAsync(cacheKey, redisValues);
            
            // Gia hạn TTL cho toàn bộ "túi" dữ liệu
            await db.KeyExpireAsync(cacheKey, TimeSpan.FromHours(1));
            
            _logger.LogInformation("Added {Count} users to room cache {RoomId} and reset TTL", userIds.Count(), roomId);
        }
    }

    /// <summary>
    /// Lấy toàn bộ danh sách thành viên của phòng (SMEMBERS)
    /// </summary>
    public async Task<IEnumerable<Guid>> GetRoomMemberIdsAsync(Guid roomId)
    {
        var db = _redis.GetDatabase();
        string cacheKey = $"room_members:{roomId}";

        // 1. Thử lấy từ Redis Set (O(1) mạng)
        var members = await db.SetMembersAsync(cacheKey);
        if (members.Length > 0)
        {
            // Gia hạn TTL
            await db.KeyExpireAsync(cacheKey, TimeSpan.FromHours(1));
            return members.Select(m => Guid.Parse(m!));
        }

        _logger.LogInformation("Cache MISS for room members {RoomId}. Fetching all from SQL.", roomId);

        // 2. Cache Miss: Query SQL một lần duy nhất cho toàn bộ member
        var memberIds = await _dbContext.RoomMembers
            .AsNoTracking()
            .Where(rm => rm.RoomId == roomId)
            .Select(rm => rm.UserId)
            .ToListAsync();

        // 3. Nạp lại vào Redis nếu có dữ liệu
        if (memberIds.Any())
        {
            var redisValues = memberIds.Select(id => (RedisValue)id.ToString()).ToArray();
            await db.SetAddAsync(cacheKey, redisValues);
            await db.KeyExpireAsync(cacheKey, TimeSpan.FromHours(1));
        }

        return memberIds;
    }
}
