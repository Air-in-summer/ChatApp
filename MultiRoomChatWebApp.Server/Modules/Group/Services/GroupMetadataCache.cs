using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using StackExchange.Redis;
using System.Text.Json;

namespace MultiRoomChatWebApp.Server.Modules.Group.Services;

/// <summary>
/// Quản lý thông tin metadata của Group bằng Redis Hash.
/// </summary>
public class GroupMetadataCache : IGroupMetadataCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<GroupMetadataCache> _logger;

    public GroupMetadataCache(
        IConnectionMultiplexer redis, 
        AppDbContext dbContext, 
        ILogger<GroupMetadataCache> logger)
    {
        _redis = redis;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Lấy metadata của Group.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tìm Hash `group_info:{groupId}`.
    /// 2. Cache Hit: Map các trường sang GroupDto, gia hạn TTL.
    /// 3. Cache Miss: Query SQL bảng Groups, lưu vào Redis Hash, gia hạn 24h.
    /// </remarks>
    public async Task<GroupDto?> GetGroupMetadataAsync(Guid groupId)
    {
        var db = _redis.GetDatabase();
        string infoKey = $"group_info:{groupId}";

        // 1. Đọc toàn bộ Hash
        var hashEntries = await db.HashGetAllAsync(infoKey);

        if (hashEntries.Length > 0)
        {
            // Cache Hit
            await db.KeyExpireAsync(infoKey, TimeSpan.FromHours(24));
            
            var dict = hashEntries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
            
            return new GroupDto
            {
                Id = groupId,
                Name = dict["Name"],
                Description = dict.GetValueOrDefault("Description"),
                IconUrl = dict.GetValueOrDefault("IconUrl"),
                InviteCode = dict["InviteCode"],
                OwnerId = Guid.Parse(dict["OwnerId"]),
                CreatedAt = DateTime.Parse(dict["CreatedAt"])
            };
        }

        _logger.LogInformation("Cache MISS for Group Metadata {GroupId}. Fallback to PostgreSQL.", groupId);

        // 2. Cache Miss: Xuống SQL
        var group = await _dbContext.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null) return null;

        // 3. Lưu vào Redis
        var entries = new HashEntry[]
        {
            new HashEntry("Name", group.Name),
            new HashEntry("Description", group.Description ?? ""),
            new HashEntry("IconUrl", group.IconUrl ?? ""),
            new HashEntry("InviteCode", group.InviteCode),
            new HashEntry("OwnerId", group.OwnerId.ToString()),
            new HashEntry("CreatedAt", group.CreatedAt.ToString("O")) // ISO 8601
        };

        await db.HashSetAsync(infoKey, entries);
        await db.KeyExpireAsync(infoKey, TimeSpan.FromHours(24));

        return new GroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            IconUrl = group.IconUrl,
            InviteCode = group.InviteCode,
            OwnerId = group.OwnerId,
            CreatedAt = group.CreatedAt
        };
    }

    public async Task InvalidateGroupMetadataAsync(Guid groupId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"group_info:{groupId}");
        _logger.LogDebug("Invalidated Group Metadata Cache for {GroupId}", groupId);
    }

    public async Task SetGroupMetadataAsync(GroupDto group)
    {
        var db = _redis.GetDatabase();
        string infoKey = $"group_info:{group.Id}";
        
        var entries = new HashEntry[]
        {
            new HashEntry("Name", group.Name),
            new HashEntry("Description", group.Description ?? ""),
            new HashEntry("IconUrl", group.IconUrl ?? ""),
            new HashEntry("InviteCode", group.InviteCode),
            new HashEntry("OwnerId", group.OwnerId.ToString()),
            new HashEntry("CreatedAt", group.CreatedAt.ToString("O"))
        };

        // Chuyển sang await để đảm bảo tính ổn định và hết warning
        await db.HashSetAsync(infoKey, entries);
        await db.KeyExpireAsync(infoKey, TimeSpan.FromHours(24));
    }

    public async Task<Guid?> GetGroupIdByInviteCodeAsync(string inviteCode)
    {
        var db = _redis.GetDatabase();
        var groupIdStr = await db.StringGetAsync($"invite_code:{inviteCode}");
        if (string.IsNullOrEmpty(groupIdStr)) return null;
        return Guid.Parse(groupIdStr!);
    }

    public async Task SetInviteCodeMappingAsync(string inviteCode, Guid groupId)
    {
        var db = _redis.GetDatabase();
        // Await thay vì Fire & Forget
        await db.StringSetAsync($"invite_code:{inviteCode}", groupId.ToString(), TimeSpan.FromDays(7));
    }

    public async Task InvalidateInviteCodeMappingAsync(string inviteCode)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"invite_code:{inviteCode}");
    }
}

