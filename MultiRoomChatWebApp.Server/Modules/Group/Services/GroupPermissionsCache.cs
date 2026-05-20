using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Group.Services;

/// <summary>
/// Quản lý phân quyền Group với Redis SETs (SADD, SISMEMBER) để đạt hiệu năng O(1).
/// </summary>
public class GroupPermissionsCache : IGroupPermissionsCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<GroupPermissionsCache> _logger;

    public GroupPermissionsCache(
        IConnectionMultiplexer redis, 
        AppDbContext dbContext, 
        ILogger<GroupPermissionsCache> logger)
    {
        _redis = redis;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Kiểm tra quyền thành viên Group.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Check Redis Hash "group_roles:{groupId}" dùng HEXISTS (O(1)).
    /// 2. Nếu Cache Hit: Trả về kết quả.
    /// 3. Nếu Cache Miss: Query PostgreSQL (chỉ lấy UserId, Role), nạp lên Hash với TTL 24h.
    /// </remarks>

    public async Task<bool> IsUserInGroupAsync(Guid groupId, Guid userId)
    {
        var db = _redis.GetDatabase();
        string roleKey = $"group_roles:{groupId}";

        // 1. Kiểm tra Cache tồn tại
        if (await db.KeyExistsAsync(roleKey))
        {
            // Gia hạn Sliding Expiration
            await db.KeyExpireAsync(roleKey, TimeSpan.FromHours(24));
            
            // 2. Cache Hit: Check O(1) bằng HEXISTS
            return await db.HashExistsAsync(roleKey, userId.ToString());
        }

        _logger.LogInformation("Cache MISS for group members {GroupId}. Fallback to PostgreSQL.", groupId);

        // 3. Cache Miss: Truy vấn DB (Tối ưu chỉ lấy UserId và Role)
        var members = await _dbContext.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.GroupId == groupId)
            .Select(gm => new { gm.UserId, gm.Role })
            .ToListAsync();

        if (members.Any())
        {
            // Nạp lên Redis Hash
            var entries = members.Select(m => new HashEntry(m.UserId.ToString(), m.Role.ToString())).ToArray();
            await db.HashSetAsync(roleKey, entries);
            await db.KeyExpireAsync(roleKey, TimeSpan.FromHours(24));

            return members.Any(m => m.UserId == userId);
        }

        return false;
    }

    public async Task InvalidateGroupMembersAsync(Guid groupId)
    {
        var db = _redis.GetDatabase();
        // Chỉ cần xóa duy nhất Key Hash
        await db.KeyDeleteAsync($"group_roles:{groupId}");
        _logger.LogDebug("Invalidated Group Roles Cache for {GroupId}", groupId);
    }

    public async Task AddUserToGroupAsync(Guid groupId, Guid userId)
    {
        var db = _redis.GetDatabase();
        string roleKey = $"group_roles:{groupId}";

        // Chỉ thêm vào Hash nếu Cache đã tồn tại. Mặc định là Member.
        if (await db.KeyExistsAsync(roleKey))
        {
            await db.HashSetAsync(roleKey, userId.ToString(), MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole.Member.ToString());
            _logger.LogDebug("Incrementally added User {UserId} as Member to Group {GroupId} cache", userId, groupId);
        }
    }

    public async Task<MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole?> GetMemberRoleAsync(Guid groupId, Guid userId)
    {
        var db = _redis.GetDatabase();
        string roleKey = $"group_roles:{groupId}";

        // 1. Thử lấy từ Hash
        var roleValue = await db.HashGetAsync(roleKey, userId.ToString());
        if (roleValue.HasValue)
        {
            return Enum.Parse<MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole>(roleValue!);
        }

        // 2. Cache Miss: Nạp toàn bộ Role của Group từ SQL (Sử dụng hàm Get plural để đồng bộ)
        var allRoles = await GetGroupMemberRolesAsync(groupId);
        return allRoles.TryGetValue(userId, out var role) ? role : null;
    }

    public async Task UpdateMemberRoleCacheAsync(Guid groupId, Guid userId, MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole role)
    {
        var db = _redis.GetDatabase();
        string roleKey = $"group_roles:{groupId}";

        // Ghi đè role mới vào Hash
        if (await db.KeyExistsAsync(roleKey))
        {
            await db.HashSetAsync(roleKey, userId.ToString(), role.ToString());
            _logger.LogDebug("Proactively updated Role for User {UserId} in Group {GroupId} cache", userId, groupId);
        }
    }

    public async Task<IDictionary<Guid, MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole>> GetGroupMemberRolesAsync(Guid groupId)
    {
        var db = _redis.GetDatabase();
        string roleKey = $"group_roles:{groupId}";

        // 1. Thử lấy toàn bộ Hash
        var hashEntries = await db.HashGetAllAsync(roleKey);
        if (hashEntries.Length > 0)
        {
            return hashEntries.ToDictionary(
                x => Guid.Parse(x.Name.ToString()),
                x => Enum.Parse<MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole>(x.Value.ToString())
            );
        }

        // 2. Cache Miss: Nạp toàn bộ từ SQL (Tối ưu Select)
        _logger.LogInformation("Cache MISS for all Member Roles in Group {GroupId}. Populating from SQL...", groupId);
        
        var members = await _dbContext.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.GroupId == groupId)
            .Select(gm => new { gm.UserId, gm.Role })
            .ToListAsync();

        if (!members.Any()) return new Dictionary<Guid, MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole>();

        var entries = members.Select(m => new HashEntry(m.UserId.ToString(), m.Role.ToString())).ToArray();
        await db.HashSetAsync(roleKey, entries);
        await db.KeyExpireAsync(roleKey, TimeSpan.FromHours(24));

        return members.ToDictionary(m => m.UserId, m => m.Role);
    }

    public async Task RemoveUserFromGroupAsync(Guid groupId, Guid userId)
    {
        var db = _redis.GetDatabase();
        string roleKey = $"group_roles:{groupId}";

        if (await db.KeyExistsAsync(roleKey))
        {
            await db.HashDeleteAsync(roleKey, userId.ToString());
            _logger.LogDebug("Removed User {UserId} from Group {GroupId} cache (HDEL)", userId, groupId);
        }
    }
}



