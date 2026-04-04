using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Room.Services;

public class RoomService : IRoomService
{
    private readonly AppDbContext _dbContext;
    private readonly IUserCacheService _userCacheService;

    public RoomService(AppDbContext dbContext, IUserCacheService userCacheService)
    {
        _dbContext = dbContext;
        _userCacheService = userCacheService;
    }

    /// <summary>
    /// Tìm DM Room hiện có hoặc tạo mới nếu chưa tồn tại.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Nếu currentUserId = targetUserId (tự chat với chính mình), throw Exception.
    /// 2. Dùng LINQ kiểm tra bảng Rooms lấy ra Type = DirectMessage mà trong đó Members chứa cả current và target.
    /// 3. Nếu tìm thấy thì trả về Room đó.
    /// 4. Nếu không, khởi tạo Room mới và Add 2 User vào RoomMembers.
    /// </remarks>
    public async Task<Core.Entities.Room> GetOrCreateDirectRoomAsync(Guid currentUserId, Guid targetUserId)
    {
        if (currentUserId == targetUserId)
            throw new ArgumentException("Cannot create a direct message room with yourself.");

        // 1. Kiểm tra User đích có tồn tại không
        bool userExists = await _dbContext.Users.AnyAsync(u => u.Id == targetUserId);
        if (!userExists)
            throw new KeyNotFoundException("Target user does not exist.");

        // 2. Query tối ưu tìm xem đã có phòng DM giữa 2 người này chưa
        // Tập các RoomId mà currentUserId tham gia (Tận dụng Index-Only Scan)
        var currentUserRooms = _dbContext.RoomMembers
            .Where(rm => rm.UserId == currentUserId)
            .Select(rm => rm.RoomId);

        // Tập các RoomId mà targetUserId tham gia
        var targetUserRooms = _dbContext.RoomMembers
            .Where(rm => rm.UserId == targetUserId)
            .Select(rm => rm.RoomId);

        // Phép Giao (INTERSECT) siêu nhanh ở tầng Database
        var commonRoomIds = currentUserRooms.Intersect(targetUserRooms);

        // Lọc bảng Rooms dựa trên tập hợp cực nhỏ các commonRoomIds
        var existingRoom = await _dbContext.Rooms
            .Include(r => r.Members)
            .Where(r => r.Type == RoomType.DirectMessage && commonRoomIds.Contains(r.Id))
            .FirstOrDefaultAsync();

        if (existingRoom != null)
        {
            return existingRoom;
        }

        // 3. Nếu chưa có, tiến hành tạo mới
        var newRoom = new Core.Entities.Room
        {
            Type = RoomType.DirectMessage,
            IsPrivate = true,
            MaxMembers = 2,
            CreatedBy = currentUserId,
            Name = null, // DM mặc định không có tên chung
            GroupId = null // Không thuộc group nào
        };

        // Thêm chính mình - DM không có khái niệm Admin, cả 2 đều bình đẳng
        newRoom.Members.Add(new RoomMember 
        { 
            UserId = currentUserId, 
            Role = RoomRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        // Thêm đối phương - cùng quyền bình đẳng
        newRoom.Members.Add(new RoomMember 
        { 
            UserId = targetUserId, 
            Role = RoomRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        _dbContext.Rooms.Add(newRoom);
        await _dbContext.SaveChangesAsync();

        return newRoom;
    }

    /// <summary>
    /// Lấy tất cả phòng của User. Kết hợp nạp UserMetadata từ Redis để đạt hiệu năng tối đa.
    /// </summary>
    public async Task<IEnumerable<Core.DTOs.RoomDto>> GetMyRoomsAsync(Guid userId)
    {
        var rawRooms = await _dbContext.RoomMembers
            .AsNoTracking()
            .Where(rm => rm.UserId == userId)
            .Include(rm => rm.Room)
                .ThenInclude(r => r.Members)
            .Select(rm => rm.Room)
            .ToListAsync();

        var result = new List<Core.DTOs.RoomDto>();

        foreach (var room in rawRooms)
        {
            var dto = new Core.DTOs.RoomDto
            {
                Id = room.Id,
                Type = room.Type,
                Name = room.Name
            };

            if (room.Type == RoomType.DirectMessage)
            {
                var otherMember = room.Members.FirstOrDefault(m => m.UserId != userId);
                if (otherMember != null)
                {
                    var otherUser = await _userCacheService.GetUserAsync(otherMember.UserId);
                    dto.OtherUserDisplayName = otherUser?.DisplayName ?? "Unknown User";
                }
            }

            result.Add(dto);
        }

        return result;
    }
}
