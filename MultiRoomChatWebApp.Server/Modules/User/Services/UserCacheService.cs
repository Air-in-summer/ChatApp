using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.User.Services;

public class UserCacheService : IUserCacheService
{
    private readonly IDistributedCache _cache;
    private readonly AppDbContext _dbContext;

    public UserCacheService(IDistributedCache cache, AppDbContext dbContext)
    {
        _cache = cache;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Lấy thông tin User cơ bản. Ưu tiên lấy từ bộ nhớ đệm Redis để đạt tốc độ < 1ms.
    /// Nếu Cache Miss, tự động Query DB và lưu ngược vào Redis cho những lần sau (Cache Hydration).
    /// </summary>
    public async Task<UserCacheDto?> GetUserAsync(Guid userId)
    {
        string cacheKey = $"user:{userId}";
        var cachedUser = await _cache.GetStringAsync(cacheKey);

        if (!string.IsNullOrEmpty(cachedUser))
        {
            return JsonSerializer.Deserialize<UserCacheDto>(cachedUser);
        }

        var dbUser = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (dbUser == null) return null;

        var dto = new UserCacheDto
        {
            Id = dbUser.Id,
            Username = dbUser.Username,
            DisplayName = dbUser.DisplayName
        };

        // Cache trong 24h
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        };

        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), options);

        return dto;
    }
}
