using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.User.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _dbContext;

    public UserService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Tìm kiếm người dùng theo Username hoặc DisplayName.
    /// Tăng tốc truy vấn tìm kiếm ngẫu nhiên bằng Trigram Index (GIN).
    /// </summary>
    /// <param name="keyword">Chuỗi tìm kiếm bất kỳ (Contains)</param>
    /// <param name="currentUserId">Loại bản thân khỏi kết quả</param>
    /// <returns>Tối đa 10 user khớp nhất</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Cắt khoảng trắng chuẩn bị chuỗi kw.
    /// 2. Áp dụng EF.Functions.ILike thay thế StartsWith. Hàm này tự bỏ qua phân biệt hoa thường.
    /// 3. Sử dụng ký tự đại diện % ở 2 đầu (Contains Search).
    /// 4. LUÔN LUÔN Bắt buộc có .OrderBy để chống lại lỗi Unpredictable Results của PostgreSQL.
    /// </remarks>
    public async Task<IEnumerable<UserSearchDto>> SearchByKeywordAsync(string keyword, Guid currentUserId)
    {
        var kw = keyword.Trim()
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        if (string.IsNullOrEmpty(kw))
            return Enumerable.Empty<UserSearchDto>();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.IsActive && 
                        u.Id != currentUserId &&
                        (EF.Functions.ILike(u.Username, $"%{kw}%") ||
                         EF.Functions.ILike(u.DisplayName, $"%{kw}%")))
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserSearchDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName
            })
            .Take(10)
            .ToListAsync();

        return users;
    }
}
