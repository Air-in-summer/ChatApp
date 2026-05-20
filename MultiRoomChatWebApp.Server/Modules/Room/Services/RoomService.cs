using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Events;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using MediatR;

namespace MultiRoomChatWebApp.Server.Modules.Room.Services;

public class RoomService : IRoomService
{
    private readonly AppDbContext _dbContext;
    private readonly IUserCacheService _userCacheService;
    private readonly IRoomMetadataCache _metadataCache;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IMediator _mediator;

    public RoomService(
        AppDbContext dbContext,
        IUserCacheService userCacheService,
        IRoomMetadataCache metadataCache,
        IRoomPermissionsCache roomPermissionsCache,
        IMediator mediator)
    {
        _dbContext = dbContext;
        _userCacheService = userCacheService;
        _metadataCache = metadataCache;
        _roomPermissionsCache = roomPermissionsCache;
        _mediator = mediator;
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
        // 1. Tối ưu: Chỉ truy vấn 1 lần bảng GroupMembers để lấy toàn bộ Roles và UserIds
        var groupMembers = await _dbContext.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.GroupId == groupId)
            .ToListAsync();

        if (!groupMembers.Any()) throw new KeyNotFoundException("Không tìm thấy thông tin thành viên Server.");

        var newRoom = new Core.Entities.Room
        {
            Name = name,
            Type = type,
            IsPrivate = isPrivate,
            CreatedBy = createdBy,
            GroupId = groupId
        };

        // 2. Áp dụng quy tắc membership dựa trên list vừa lấy
        var membersToAdd = new List<RoomMember>();
        
        // Tìm Owner từ list (để đảm bảo Owner luôn có mặt kể cả phòng Private)
        var owner = groupMembers.FirstOrDefault(m => m.Role == MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole.Owner);

        if (isPrivate)
        {
            // [PHÒNG PRIVATE]: Chỉ người tạo + Owner
            var filteredIds = new HashSet<Guid> { createdBy };
            if (owner != null) filteredIds.Add(owner.UserId);

            foreach (var uid in filteredIds)
            {
                // Người tạo hoặc Owner đều gán quyền Admin trong phòng này
                membersToAdd.Add(new RoomMember
                {
                    UserId = uid,
                    Role = RoomRole.Admin,
                    JoinedAt = DateTime.UtcNow
                });
            }
        }
        else
        {
            // [PHÒNG PUBLIC]: Thêm tất cả thành viên trong nhóm
            foreach (var gm in groupMembers)
            {
                // Gán Role: Người tạo là Admin, Owner là Admin, còn lại là Member
                var role = (gm.UserId == createdBy || gm.Role == MultiRoomChatWebApp.Server.Modules.Group.Core.Enums.GroupRole.Owner) 
                    ? RoomRole.Admin 
                    : RoomRole.Member;

                membersToAdd.Add(new RoomMember
                {
                    UserId = gm.UserId,
                    Role = role,
                    JoinedAt = DateTime.UtcNow
                });
            }
        }

        newRoom.Members = membersToAdd;

        _dbContext.Rooms.Add(newRoom);
        await _dbContext.SaveChangesAsync();

        // [PROACTIVE CACHE WARM-UP] Nạp Metadata phòng lên Redis ngay lập tức
        await _metadataCache.SetRoomMetadataAsync(newRoom.Id, newRoom.GroupId, newRoom.IsPrivate, newRoom.Type);

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
                IsPrivate = rm.Room.IsPrivate,
                GroupId = rm.Room.GroupId,
                
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
                    Name = projection.Name,
                    IsPrivate = projection.IsPrivate,
                    GroupId = projection.GroupId
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

    /// <summary>
    /// Thêm User vào bảng RoomMembers của tất cả các phòng Public trong một Group.
    /// Được gọi bởi GroupService ngay sau khi User join Group thành công.
    /// </summary>
    /// <param name="groupId">ID của Group vừa tham gia</param>
    /// <param name="userId">ID của User mới tham gia</param>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Query tất cả các phòng Public (IsPrivate = false) thuộc Group.
    /// 2. Lọc ra những phòng User chưa là thành viên (tránh duplicate key).
    /// 3. Bulk insert các bản ghi RoomMember mới.
    ///
    /// Lưu ý:
    /// - Không cập nhật Room Cache (chủ đích: phòng Public không dùng Room Cache).
    /// - Caller (GroupService) chịu trách nhiệm gọi hàm này sau khi đã lưu GroupMember.
    /// </remarks>
    public async Task AddUserToPublicRoomsAsync(Guid groupId, Guid userId)
    {
        // 1. Lấy ID các phòng Public trong Group
        var publicRoomIds = await _dbContext.Rooms
            .AsNoTracking()
            .Where(r => r.GroupId == groupId && !r.IsPrivate)
            .Select(r => r.Id)
            .ToListAsync();

        if (!publicRoomIds.Any()) return;

        // 2. Tạo bản ghi RoomMember cho tất cả các phòng Public tìm thấy
        // (Giả định dữ liệu cũ đã được dọn sạch khi User rời/bị kick khỏi Group)
        var newMembers = publicRoomIds.Select(roomId => new RoomMember
        {
            RoomId = roomId,
            UserId = userId,
            Role = RoomRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        _dbContext.RoomMembers.AddRange(newMembers);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Thêm danh sách thành viên vào một phòng Private.
    /// Caller (GroupController) đã check quyền Owner/Admin và validate userIds thuộc Group.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Validate phòng: Lấy metadata từ IRoomMetadataCache → xác nhận IsPrivate == true và có GroupId.
    /// 2. Lọc trùng SQL: Loại bỏ userId đã có trong RoomMembers (tránh duplicate key).
    /// 3. Bulk Insert vào bảng RoomMembers (Role = Member).
    /// 4. Cập nhật Cache: Gọi IRoomPermissionsCache.AddUsersToRoomCacheAsync (SADD batch).
    /// 5. Publish Domain Event để Notification Module bắn SignalR.
    /// </remarks>
    public async Task AddMembersToPrivateRoomAsync(Guid roomId, IEnumerable<Guid> userIds)
    {
        if (userIds == null || !userIds.Any()) return;

        // 1. Validate phòng: Phải là Private và thuộc một Group
        var metadata = await _metadataCache.GetRoomMetadataAsync(roomId);
        if (metadata == null || !metadata.Value.IsPrivate || metadata.Value.GroupId == null)
        {
            throw new KeyNotFoundException("Phòng không tồn tại hoặc không phải là phòng Private trong Group.");
        }

        var groupId = metadata.Value.GroupId.Value;

        // 2. Lọc trùng: Loại bỏ những người đã ở trong phòng (Tận dụng Cache - O(1))
        var currentMemberIds = await _roomPermissionsCache.GetRoomMemberIdsAsync(roomId);
        var existingSet = new HashSet<Guid>(currentMemberIds);

        var newUserIds = userIds
            .Distinct()
            .Where(uid => !existingSet.Contains(uid))
            .ToList();

        if (!newUserIds.Any()) return;

        // 3. Bulk Insert vào bảng RoomMembers
        var newMembers = newUserIds.Select(uid => new RoomMember
        {
            RoomId = roomId,
            UserId = uid,
            Role = RoomRole.Member,
            JoinedAt = DateTime.UtcNow
        });

        _dbContext.RoomMembers.AddRange(newMembers);
        await _dbContext.SaveChangesAsync();

        // 4. Cập nhật Cache (Proactive SADD — chỉ thêm nếu cache đang ấm)
        await _roomPermissionsCache.AddUsersToRoomCacheAsync(roomId, newUserIds);

        // 5. Bắn Domain Event để Notification Handler gửi SignalR tới những người vừa được add
        await _mediator.Publish(new Core.Events.UsersAddedToPrivateRoomEvent(roomId, groupId, newUserIds));
    }

    /// <summary>
    /// Lấy danh sách ID thành viên hiện tại của phòng.
    /// Tận dụng trực tiếp Cache (Redis SMEMBERS) để đạt hiệu năng O(1).
    /// </summary>
    public async Task<IEnumerable<Guid>> GetRoomMemberIdsAsync(Guid roomId)
    {
        return await _roomPermissionsCache.GetRoomMemberIdsAsync(roomId);
    }
}
