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

        // Tối ưu hóa Cache (Rule #6):
        // - SlidingExpiration: Nếu trong 1 tiếng không ai "sờ" tới User này, Redis sẽ tự dọn dẹp để tiết kiệm RAM.
        // - AbsoluteExpiration: Ngay cả khi có người xem liên tục, sau 12 tiếng vẫn bắt buộc nạp lại từ DB để tránh dữ liệu quá cũ.
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
        };

        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), options);

        return dto;
    }

    /// <summary>
    /// Xóa bộ nhớ đệm của User. 
    /// </summary>
    /// <remarks>
    /// BẮT BUỘC gọi hàm này ngay sau khi thực hiện thành công các thao tác:
    /// 1. Cập nhật DisplayName.
    /// 2. Cập nhật Username.
    /// 3. Cập nhật AvatarUrl (nếu có).
    /// 4. Thay đổi quyền hạn hoặc trạng thái tài khoản.
    /// 5. Xóa tài khoản (Soft Delete).
    /// </remarks>
    public async Task InvalidateUserAsync(Guid userId)
    {
        string cacheKey = $"user:{userId}";
        await _cache.RemoveAsync(cacheKey);
    }
}
