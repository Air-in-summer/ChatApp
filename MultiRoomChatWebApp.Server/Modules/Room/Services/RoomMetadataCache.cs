using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Room.Services;

/// <summary>
/// Quản lý thông tin phân loại Room bằng Redis Hash.
/// </summary>
public class RoomMetadataCache : IRoomMetadataCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<RoomMetadataCache> _logger;

    public RoomMetadataCache(
        IConnectionMultiplexer redis, 
        AppDbContext dbContext, 
        ILogger<RoomMetadataCache> logger)
    {
        _redis = redis;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Lấy metadata phòng (GroupId, IsPrivate).
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tìm Hash `room_info:{roomId}`.
    /// 2. Cache Hit: Trả về kết quả, gia hạn TTL.
    /// 3. Cache Miss: Query SQL bảng Rooms, lưu vào Redis Hash, gia hạn 24h.
    /// </remarks>
    public async Task<(Guid? GroupId, bool IsPrivate, RoomType Type)?> GetRoomMetadataAsync(Guid roomId)
    {
        var db = _redis.GetDatabase();
        string infoKey = $"room_info:{roomId}";

        // Đọc từng trường (HashGet)
        string? groupIdStr = await db.HashGetAsync(infoKey, "GroupId");
        string? isPrivateStr = await db.HashGetAsync(infoKey, "IsPrivate");
        string? typeStr = await db.HashGetAsync(infoKey, "Type");

        if (TryParseCachedMetadata(groupIdStr, isPrivateStr, typeStr, out var cachedGroupId, out var cachedIsPrivate, out var cachedType))
        {
            // Cache Hit: TTL 24 tiếng vì thông tin này gần như là tĩnh
            await db.KeyExpireAsync(infoKey, TimeSpan.FromHours(24));
            
            return (cachedGroupId, cachedIsPrivate, cachedType);
        }

        _logger.LogInformation("Cache MISS for Room Metadata {RoomId}. Fallback to PostgreSQL.", roomId);

        // Cache Miss: Xuống SQL lấy thông tin cấu trúc
        var roomInfo = await _dbContext.Rooms
            .AsNoTracking()
            .Where(r => r.Id == roomId)
            .Select(r => new { r.GroupId, r.IsPrivate, r.Type })
            .FirstOrDefaultAsync();

        if (roomInfo == null)
            return null; // Room không tồn tại

        // Lưu vào Redis
        await db.HashSetAsync(infoKey, new HashEntry[] {
            new HashEntry("GroupId", roomInfo.GroupId?.ToString() ?? "none"),
            new HashEntry("IsPrivate", roomInfo.IsPrivate.ToString()),
            new HashEntry("Type", roomInfo.Type.ToString())
        });
        
        await db.KeyExpireAsync(infoKey, TimeSpan.FromHours(24));

        return (roomInfo.GroupId, roomInfo.IsPrivate, roomInfo.Type);
    }

    public async Task InvalidateRoomMetadataAsync(Guid roomId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"room_info:{roomId}");
        _logger.LogDebug("Invalidated Room Metadata Cache for {RoomId}", roomId);
    }

    public async Task SetRoomMetadataAsync(Guid roomId, Guid? groupId, bool isPrivate, RoomType type)
    {
        var db = _redis.GetDatabase();
        string infoKey = $"room_info:{roomId}";

        var entries = new HashEntry[]
        {
            new HashEntry("GroupId", groupId?.ToString() ?? "none"),
            new HashEntry("IsPrivate", isPrivate.ToString()),
            new HashEntry("Type", type.ToString())
        };

        // Await tường minh để đảm bảo dữ liệu và hết warning
        await db.HashSetAsync(infoKey, entries);
        await db.KeyExpireAsync(infoKey, TimeSpan.FromHours(24));
        
        _logger.LogDebug("Proactively cached Metadata for Room {RoomId}", roomId);
    }

    private static bool TryParseCachedMetadata(
        string? groupIdStr,
        string? isPrivateStr,
        string? typeStr,
        out Guid? groupId,
        out bool isPrivate,
        out RoomType type)
    {
        groupId = null;
        isPrivate = false;
        type = default;

        if (groupIdStr == null || isPrivateStr == null || typeStr == null)
        {
            return false;
        }

        if (!bool.TryParse(isPrivateStr, out isPrivate))
        {
            return false;
        }

        if (!Enum.TryParse(typeStr, ignoreCase: true, out type))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(groupIdStr) || groupIdStr == "none")
        {
            return true;
        }

        if (!Guid.TryParse(groupIdStr, out var parsedGroupId))
        {
            return false;
        }

        groupId = parsedGroupId;
        return true;
    }
}
