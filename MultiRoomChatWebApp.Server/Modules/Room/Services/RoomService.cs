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

        // Phép Giao (INTERSECT) 
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
    /// Tạo một Room (Channel) mới bên trong một Group/Server.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Khởi tạo Entity Room gắn với GroupId.
    /// 2. Thêm người tạo vào danh sách Members của Room với quyền Admin.
    /// 3. Lưu vào Database (Sử dụng Navigation Property để EF tự xử lý quan hệ).
    /// </remarks>
    public async Task<Core.Entities.Room> CreateGroupRoomAsync(string name, RoomType type, bool isPrivate, Guid createdBy, Guid groupId)
    {
        var newRoom = new Core.Entities.Room
        {
            Name = name,
            Type = type,
            IsPrivate = isPrivate,
            CreatedBy = createdBy,
            GroupId = groupId
        };

        // Gán người tạo làm Admin của phòng này (Dùng pattern Members.Add tương tự DM)
        newRoom.Members.Add(new RoomMember
        {
            UserId = createdBy,
            Role = RoomRole.Admin,
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
        // [BƯỚC 1: TRUY VẤN DATABASE BẰNG PROJECTION]
        // Thay vì dùng .Include() kéo toàn bộ Entity Room và hàng ngàn RoomMember vào RAM,
        // ta dùng .Select() để chỉ định chính xác những cột cần lấy.
        // EF Core sẽ dịch đoạn này thành 1 câu SQL cực kỳ tối ưu.
        var roomProjections = await _dbContext.RoomMembers
            .AsNoTracking()
            .Where(rm => rm.UserId == userId)
            .Select(rm => new
            {
                // Lấy thông tin cơ bản của phòng
                RoomId = rm.Room.Id,
                Type = rm.Room.Type,
                Name = rm.Room.Name,
                
                // Mấu chốt tối ưu: Nếu là phòng DM, ta chỉ SELECT ra đúng 1 cái UserId của người đối diện.
                // Không cần tải toàn bộ danh sách Members của phòng đó.
                OtherUserId = rm.Room.Type == RoomType.DirectMessage
                    ? rm.Room.Members
                        .Where(m => m.UserId != userId)
                        .Select(m => (Guid?)m.UserId)
                        .FirstOrDefault()
                    : null
            })
            .ToListAsync();

        // [BƯỚC 2: TẠO DANH SÁCH CÁC TASK GỌI REDIS SONG SONG]
        // Chuẩn bị một danh sách chứa các Task (chưa chạy await ngay lập tức)
        var hydrationTasks = new List<Task<Core.DTOs.RoomDto>>();

        foreach (var projection in roomProjections)
        {
            // Tạo một Func (closure) để xử lý từng phòng độc lập
            hydrationTasks.Add(Task.Run(async () =>
            {
                // Khởi tạo DTO cơ bản
                var dto = new Core.DTOs.RoomDto
                {
                    Id = projection.RoomId,
                    Type = projection.Type,
                    Name = projection.Name
                };

                // Nếu là phòng DM và có ID người đối diện, tiến hành gọi Redis
                if (projection.Type == RoomType.DirectMessage && projection.OtherUserId.HasValue)
                {
                    // Gọi Cache Service (Lúc này Task mới bắt đầu chạy thực sự)
                    var otherUser = await _userCacheService.GetUserAsync(projection.OtherUserId.Value);
                    
                    // Đắp thông tin vào DTO
                    dto.OtherUserDisplayName = otherUser?.DisplayName ?? "Unknown";
                    dto.OtherUserUsername = otherUser?.Username ?? "unknown";
                }

                return dto;
            }));
        }

        // [BƯỚC 3: THỰC THI TẤT CẢ TASK CÙNG LÚC]
        // Task.WhenAll sẽ bắn toàn bộ 50 request lên Redis cùng một thời điểm.
        // Tổng thời gian chờ chỉ bằng thời gian của request chậm nhất (VD: 2ms thay vì 100ms).
        var result = await Task.WhenAll(hydrationTasks);

        return result;
    }
}
