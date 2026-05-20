using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;
using GroupEntity = MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.Group;
using GroupMemberEntity = MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.GroupMember;
using Microsoft.Extensions.Logging;

using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Room.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Group.Services;

public class GroupService : IGroupService
{
    private readonly AppDbContext _context;
    private readonly IRoomService _roomService;
    private readonly IGroupPermissionsCache _permissionsCache;
    private readonly IGroupMetadataCache _metadataCache;
    private readonly ILogger<GroupService> _logger;
    private readonly IUserCacheService _userCacheService;
    private readonly MediatR.IMediator _mediator;


    public GroupService(
        AppDbContext context, 
        IRoomService roomService, 
        IGroupPermissionsCache permissionsCache,
        IGroupMetadataCache metadataCache,
        ILogger<GroupService> logger,
        IUserCacheService userCacheService,
        MediatR.IMediator mediator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _permissionsCache = permissionsCache ?? throw new ArgumentNullException(nameof(permissionsCache));
        _metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userCacheService = userCacheService ?? throw new ArgumentNullException(nameof(userCacheService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// Tạo một Server mới và cấp quyền Owner cho người tạo.
    /// </summary>
    /// <param name="userId">ID của người dùng đang đăng nhập</param>
    /// <param name="request">Thông tin Server (Tên, mô tả, ảnh đại diện)</param>
    /// <returns>DTO chứa thông tin Server vừa tạo, bao gồm mã InviteCode</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Sinh ngẫu nhiên InviteCode (6 ký tự) và đảm bảo không trùng lặp trong DB.
    /// 2. Tạo Entity Group (Server) với thông tin từ request.
    /// 3. Tạo Entity GroupMember để gán người tạo làm Owner.
    /// 4. Lưu tất cả vào Database trong một Transaction ẩn của EF Core.
    /// 
    /// Lưu ý:
    /// - InviteCode được sinh ra có thể được dùng làm link mời (VD: /join/{inviteCode}).
    /// </remarks>
    public async Task<GroupDto> CreateGroupAsync(Guid userId, CreateGroupRequest request)
    {
        // 1. Sinh mã InviteCode duy nhất
        string inviteCode = await GenerateUniqueInviteCodeAsync();

        // 2. Khởi tạo Entity Server
        var newGroup = new GroupEntity
        {
            Name = request.Name,
            Description = request.Description,
            IconUrl = request.IconUrl,
            InviteCode = inviteCode,
            OwnerId = userId
        };

        // 3. Khởi tạo Entity Thành viên cấp Owner
        var groupMember = new GroupMemberEntity
        {
            GroupId = newGroup.Id,
            UserId = userId,
            Role = GroupRole.Owner
        };

        // 4. Khởi tạo Transaction (Bảo vệ tính toàn vẹn dữ liệu)
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Đẩy Server vào DbContext
            _context.Groups.Add(newGroup);
            _context.GroupMembers.Add(groupMember);
            await _context.SaveChangesAsync(); // Commit 1 (Tạo ra ID cho Group)

            // 5. Khởi tạo Channel mặc định (#general) qua RoomService
            var defaultRoom = await _roomService.CreateGroupRoomAsync(
                name: "general",
                type: MultiRoomChatWebApp.Server.Modules.Room.Core.Enums.RoomType.Text,
                isPrivate: false,
                createdBy: userId,
                groupId: newGroup.Id
            );

            // Xác nhận toàn bộ tiến trình
            await transaction.CommitAsync();
            _logger.LogInformation("User {UserId} created Server '{GroupName}' (Id: {GroupId}) and default channel {RoomId}", userId, newGroup.Name, newGroup.Id, defaultRoom.Id);

            // [PROACTIVE CACHE WARM-UP] Nạp dữ liệu lên Redis ngay lập tức
            var groupDto = new GroupDto
            {
                Id = newGroup.Id,
                Name = newGroup.Name,
                Description = newGroup.Description,
                IconUrl = newGroup.IconUrl,
                InviteCode = newGroup.InviteCode,
                OwnerId = newGroup.OwnerId,
                CreatedAt = newGroup.CreatedAt
            };

            await _metadataCache.SetGroupMetadataAsync(groupDto);
            await _metadataCache.SetInviteCodeMappingAsync(newGroup.InviteCode, newGroup.Id);
            // (Lưu ý: _roomService.CreateGroupRoomAsync đã lo việc SQL. Bây giờ ta gọi qua IRoomMetadataCache để Warm-up cho phòng này.
            // Nhưng hiện tại GroupService chưa Inject IRoomMetadataCache. Ta có thể bỏ qua bước này hoặc để RoomService tự lo.)

            return groupDto;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi lưu Server mới vào cơ sở dữ liệu. Đã Rollback.");
            throw; 
        }
    }


    /// <summary>
    /// Hàm hỗ trợ sinh mã InviteCode 6 ký tự và kiểm tra chống trùng lặp.
    /// </summary>
    private async Task<string> GenerateUniqueInviteCodeAsync()
    {
        string code = string.Empty;
        bool isUnique = false;

        while (!isUnique)
        {
            code = MultiRoomChatWebApp.Server.Shared.Extensions.StringExtensions.GenerateRandomString(6);

            // Đảm bảo mã chưa từng được sử dụng (Kể cả các Server đã bị Soft Delete)
            bool exists = await _context.Groups.IgnoreQueryFilters().AnyAsync(g => g.InviteCode == code);
            if (!exists)
            {
                isUnique = true;
            }
        }

        return code;
    }

    /// <summary>
    /// Tạo một Channel (Room) mới trong Server.
    /// Yêu cầu người tạo phải là Owner hoặc Admin của Server.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Kiểm tra User có nằm trong Server không và quyền hạn có đủ không (Owner/Admin).
    /// 2. Tạo Entity Room với GroupId tương ứng.
    /// 3. Lưu xuống Database.
    /// </remarks>
    public async Task<Guid> CreateGroupChannelAsync(Guid userId, Guid groupId, CreateGroupChannelRequest request)
    {
        // 1. Kiểm tra quyền qua Cache (O(1))
        var role = await _permissionsCache.GetMemberRoleAsync(groupId, userId);

        if (role == null)
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên của Server này.");
        }

        if (role != GroupRole.Owner && role != GroupRole.Admin)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền tạo Kênh trong Server này.");
        }

        // 2. Gọi RoomService để tạo Room
        try
        {
            var newRoom = await _roomService.CreateGroupRoomAsync(
                name: request.Name,
                type: request.Type,
                isPrivate: request.IsPrivate,
                createdBy: userId,
                groupId: groupId
            );

            _logger.LogInformation("Admin/Owner {UserId} created Channel {RoomId} in Server {GroupId}", userId, newRoom.Id, groupId);

            // [EVENT] Bắn sự kiện để Notification Module thông báo tới các member (trừ người tạo)
            var memberRoles = await _permissionsCache.GetGroupMemberRolesAsync(groupId);
            var memberIds = memberRoles.Keys;

            await _mediator.Publish(new Core.Events.RoomCreatedInGroupEvent(groupId, newRoom.Id, request.Name, memberIds));

            return newRoom.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tạo Channel cho Server {GroupId}", groupId);
            throw;
        }
    }


    /// <summary>
    /// Tham gia vào Server thông qua mã mời (Invite Code).
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tìm kiếm Server dựa trên mã mời (inviteCode). Bỏ qua các Server đã bị Soft Delete.
    /// 2. Kiểm tra xem User đã là thành viên của Server chưa.
    /// 3. Nếu chưa, tạo bản ghi GroupMember mới với quyền Member.
    /// 4. Lưu vào Database và trả về thông tin Server.
    /// </remarks>
    public async Task<GroupDto> JoinGroupByInviteCodeAsync(Guid userId, string inviteCode)
    {
        // 1. TÌM GROUPID TỪ CACHE 
        var cachedGroupId = await _metadataCache.GetGroupIdByInviteCodeAsync(inviteCode);
        Guid groupId;

        if (cachedGroupId.HasValue)
        {
            groupId = cachedGroupId.Value;
        }
        else
        {
            // Cache Miss: Xuống DB tìm và nạp lại vào Cache
            var dbGroup = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.InviteCode == inviteCode);
            if (dbGroup == null) throw new KeyNotFoundException("Mã mời không hợp lệ hoặc Server không còn tồn tại.");
            
            groupId = dbGroup.Id;
            await _metadataCache.SetInviteCodeMappingAsync(inviteCode, groupId);
        }

        // 2. Lấy thông tin Group (Ưu tiên Cache)
        var groupMetadata = await _metadataCache.GetGroupMetadataAsync(groupId);
        if (groupMetadata == null) throw new KeyNotFoundException("Server không tồn tại.");

        // 3. Kiểm tra User đã join chưa (Ưu tiên Cache)
        bool isAlreadyMember = await _permissionsCache.IsUserInGroupAsync(groupId, userId);

        if (isAlreadyMember)
        {
            _logger.LogInformation("User {UserId} is already a member of Server {GroupId}", userId, groupId);
        }
        else
        {
            // 4. Nếu chưa Join: Lưu DB và Cập nhật Cache gia tăng
            var newMember = new GroupMemberEntity
            {
                GroupId = groupId,
                UserId = userId,
                Role = GroupRole.Member
            };

            _context.GroupMembers.Add(newMember);

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("User {UserId} joined Server {GroupId} via InviteCode {InviteCode}", userId, groupId, inviteCode);

                // [INCREMENTAL UPDATE] Nhét thêm User này vào Cache Group thay vì xóa sập toàn bộ Cache cũ
                await _permissionsCache.AddUserToGroupAsync(groupId, userId);

                // [ROOM MEMBERSHIP] Thêm User vào tất cả phòng Public trong Group
                // Gọi trực tiếp qua _roomService (đã inject sẵn) - logic thuộc Room Module
                await _roomService.AddUserToPublicRoomsAsync(groupId, userId);

                // [EVENT] Bắn sự kiện để các module khác (Notification) xử lý
                await _mediator.Publish(new Core.Events.MemberJoinedGroupEvent(groupId, userId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm User {UserId} vào Server {GroupId}", userId, groupId);
                throw;
            }
        }

        // 5. Trả về thông tin
        return groupMetadata;
    }


    /// <summary>
    /// Lấy danh sách các Server mà User đang tham gia.
    /// Tối ưu hiệu năng bằng cách bù Metadata từ Redis song song.
    /// </summary>
    public async Task<IEnumerable<GroupDto>> GetMyGroupsAsync(Guid userId)
    {
        // 1. Lấy danh sách ID các Group từ SQL (Chỉ lấy ID để tối ưu)
        var groupIds = await _context.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.UserId == userId)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        if (!groupIds.Any()) return Enumerable.Empty<GroupDto>();

        // 2. Hydration: Gọi Redis song song để lấy thông tin chi tiết từng Group
        var hydrationTasks = groupIds.Select(id => _metadataCache.GetGroupMetadataAsync(id));
        
        var results = await Task.WhenAll(hydrationTasks);

        // 3. Lọc bỏ các kết quả null (phòng trường hợp DB có mà Cache lỗi hoặc Group bị xóa chưa sạch)
        return results.Where(g => g != null)!;
    }

    /// <summary>
    /// Lấy danh sách các Room (Channel) trong một Group.
    /// </summary>
    /// <param name="groupId">ID của Server</param>
    /// <returns>Danh sách RoomDto</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Truy vấn trực tiếp từ SQL (AsNoTracking) vì số lượng kênh ít và cần sắp xếp theo thời gian tạo.
    /// 2. Sắp xếp tăng dần theo CreatedAt để đảm bảo thứ tự hiển thị kênh.
    /// </remarks>
    public async Task<IEnumerable<RoomDto>> GetGroupRoomsAsync(Guid groupId, Guid userId)
    {
        // [OPTIMIZED JOIN] Dùng RoomMembers làm điểm neo để filter theo quyền truy cập của User.
        // EF Core sẽ biên dịch thành SQL INNER JOIN cực kỳ hiệu quả, tận dụng Composite Index.
        return await _context.RoomMembers
            .AsNoTracking()
            .Where(rm => rm.UserId == userId && rm.Room.GroupId == groupId)
            .OrderBy(rm => rm.Room.CreatedAt)
            .Select(rm => new RoomDto
            {
                Id = rm.Room.Id,
                Name = rm.Room.Name,
                Type = rm.Room.Type,
                IsPrivate = rm.Room.IsPrivate,
                GroupId = rm.Room.GroupId
            })
            .ToListAsync();
    }

    /// <summary>
    /// Lấy danh sách thành viên trong một Group kèm theo vai trò.
    /// </summary>
    /// <param name="groupId">ID của Server</param>
    /// <returns>Danh sách GroupMemberDto (Profile + Role)</returns>
    /// <remarks>
    /// Luồng xử lý (Hydration Pattern):
    /// 1. Lấy toàn bộ cặp (UserId, Role) từ Redis Hash "group_roles:{groupId}" (O(1)).
    /// 2. Nếu Cache Miss: Tự động nạp từ SQL lên Redis (Xử lý bên trong Cache Service).
    /// 3. Với mỗi UserId, gọi UserCacheService để lấy Profile (Avatar, DisplayName) song song (Task.WhenAll).
    /// 4. Gộp (Merge) Profile và Role lại thành GroupMemberDto.
    /// 5. Sắp xếp danh sách theo cấp bậc Role (Owner -> Admin -> Member).
    /// </remarks>
    public async Task<IEnumerable<GroupMemberDto>> GetGroupMembersAsync(Guid groupId)
    {
        // 1. Lấy toàn bộ danh sách (UserId, Role) từ Redis (O(1))
        var memberRoles = await _permissionsCache.GetGroupMemberRolesAsync(groupId);

        if (!memberRoles.Any()) return Enumerable.Empty<GroupMemberDto>();

        // 2. Hydrate Profile song song từ Redis qua UserCacheService
        var hydrationTasks = memberRoles.Select(async pair => 
        {
            var profile = await _userCacheService.GetUserAsync(pair.Key);
            return profile != null ? new GroupMemberDto { Profile = profile, Role = pair.Value } : null;
        });

        var results = await Task.WhenAll(hydrationTasks);

        // Lọc bỏ null và sắp xếp theo vai trò (Owner -> Admin -> Member)
        return results.Where(r => r != null)
            .OrderBy(r => r!.Role)
            .Select(r => r!);
    }

    /// <summary>
    /// Cập nhật thông tin cơ bản của Server (Tên, Mô tả, Ảnh đại diện).
    /// </summary>
    /// <param name="userId">ID người thực hiện</param>
    /// <param name="groupId">ID Server cần cập nhật</param>
    /// <param name="request">Thông tin cập nhật mới</param>
    /// <returns>GroupDto đã cập nhật</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Kiểm tra quyền: Chỉ Owner mới có quyền thay đổi Metadata của Server.
    /// 2. Cập nhật dữ liệu vào SQL PostgreSQL.
    /// 3. Cập nhật chủ động (Proactive Warm-up) vào Redis Metadata Cache để đảm bảo tính nhất quán.
    /// </remarks>
    public async Task<GroupDto> UpdateGroupAsync(Guid userId, Guid groupId, UpdateGroupRequest request)
    {
        // 1. Kiểm tra quyền Owner
        var role = await _permissionsCache.GetMemberRoleAsync(groupId, userId);
        if (role != GroupRole.Owner)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ sở hữu mới có quyền thay đổi thông tin Server.");
        }

        // 2. Cập nhật SQL
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null) throw new KeyNotFoundException("Không tìm thấy Server.");

        if (!string.IsNullOrWhiteSpace(request.Name)) group.Name = request.Name;
        if (request.Description != null) group.Description = request.Description;
        if (request.IconUrl != null) group.IconUrl = request.IconUrl;
        
        group.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // 3. Update Cache (Warm-up)
        var updatedDto = new GroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            IconUrl = group.IconUrl,
            InviteCode = group.InviteCode,
            OwnerId = group.OwnerId,
            CreatedAt = group.CreatedAt
        };

        await _metadataCache.SetGroupMetadataAsync(updatedDto);
        _logger.LogInformation("Owner {UserId} updated metadata for Server {GroupId}", userId, groupId);

        return updatedDto;
    }

    /// <summary>
    /// Bổ nhiệm hoặc bãi miễn vai trò Admin cho một thành viên.
    /// </summary>
    /// <param name="ownerId">ID của Chủ sở hữu thực hiện</param>
    /// <param name="groupId">ID Server</param>
    /// <param name="targetUserId">ID thành viên bị tác động</param>
    /// <param name="newRole">Vai trò mới (Admin/Member)</param>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Kiểm tra quyền: Chỉ Owner mới có quyền quản lý chức vụ thành viên.
    /// 2. Validate: Không thể dùng hàm này để gán quyền Owner (phải dùng Transfer Ownership).
    /// 3. Cập nhật SQL bảng GroupMembers.
    /// 4. Cập nhật chủ động (Proactive Update) vào Redis Hash "group_roles" để UI hiển thị đúng ngay lập tức.
    /// </remarks>
    public async Task UpdateMemberRoleAsync(Guid ownerId, Guid groupId, Guid targetUserId, GroupRole newRole)
    {
        // 1. Check quyền người thực hiện
        var callerRole = await _permissionsCache.GetMemberRoleAsync(groupId, ownerId);
        if (callerRole != GroupRole.Owner)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ sở hữu mới có quyền quản lý vai trò thành viên.");
        }

        if (newRole == GroupRole.Owner)
        {
            throw new InvalidOperationException("Vui lòng sử dụng tính năng Chuyển nhượng quyền sở hữu để thay đổi Owner.");
        }

        // 2. Cập nhật SQL
        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == targetUserId);
            
        if (member == null) throw new KeyNotFoundException("User không phải là thành viên của Server.");

        var oldRole = member.Role;
        member.Role = newRole;
        await _context.SaveChangesAsync();

        // 3. Proactive Cache Update: Cập nhật Role mới lên Redis ngay lập tức
        await _permissionsCache.UpdateMemberRoleCacheAsync(groupId, targetUserId, newRole);

        // [EVENT] Bắn sự kiện để Notification Module báo cho User biết
        await _mediator.Publish(new Core.Events.MemberRoleUpdatedEvent(groupId, targetUserId, oldRole, newRole, ownerId));

        _logger.LogInformation("Owner {OwnerId} changed Role of User {TargetUserId} to {NewRole} in Server {GroupId}", ownerId, targetUserId, newRole, groupId);
    }

    /// <summary>
    /// Chuyển nhượng quyền sở hữu tối cao (Server Ownership).
    /// </summary>
    /// <param name="currentOwnerId">ID Chủ sở hữu hiện tại</param>
    /// <param name="groupId">ID Server</param>
    /// <param name="newOwnerId">ID thành viên sẽ nhận quyền sở hữu</param>
    /// <remarks>
    /// Luồng xử lý (Atomic Transaction):
    /// 1. Validate: Người thực hiện phải là Owner hiện tại và người nhận phải là thành viên trong Server.
    /// 2. Bắt đầu DB Transaction.
    /// 3. Cập nhật bảng Groups: Thay đổi OwnerId.
    /// 4. Cập nhật bảng GroupMembers: Hạ cấp Owner cũ xuống Admin, nâng cấp Member mới lên Owner.
    /// 5. Commit Transaction.
    /// 6. Đồng bộ Cache: Cập nhật Metadata (OwnerId mới) và cập nhật 2 Role mới trong Redis Hash "group_roles".
    /// </remarks>
    public async Task TransferOwnershipAsync(Guid currentOwnerId, Guid groupId, Guid newOwnerId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Kiểm tra tính hợp lệ
            var group = await _context.Groups.FindAsync(groupId);
            if (group == null || group.OwnerId != currentOwnerId)
            {
                throw new UnauthorizedAccessException("Bạn không phải là chủ sở hữu hiện tại.");
            }

            var newOwnerMember = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == newOwnerId);
            
            if (newOwnerMember == null)
            {
                throw new InvalidOperationException("Người nhận quyền sở hữu phải là thành viên của Server.");
            }

            // 2. Cập nhật Group Table
            group.OwnerId = newOwnerId;
            group.UpdatedAt = DateTime.UtcNow;

            // 3. Cập nhật Role Table (Old Owner -> Admin, New Owner -> Owner)
            var oldOwnerMember = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == currentOwnerId);
            
            if (oldOwnerMember != null) oldOwnerMember.Role = GroupRole.Admin;
            newOwnerMember.Role = GroupRole.Owner;


            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 4. Proactive Cache Update: Cập nhật đồng thời Metadata Group và Role thành viên
            var updatedGroupDto = new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                IconUrl = group.IconUrl,
                InviteCode = group.InviteCode,
                OwnerId = group.OwnerId,
                CreatedAt = group.CreatedAt
            };

            await _metadataCache.SetGroupMetadataAsync(updatedGroupDto);
            await _permissionsCache.UpdateMemberRoleCacheAsync(groupId, currentOwnerId, GroupRole.Admin);
            await _permissionsCache.UpdateMemberRoleCacheAsync(groupId, newOwnerId, GroupRole.Owner);

            // [EVENT] Báo cho toàn bộ member biết Server có chủ mới
            await _mediator.Publish(new Core.Events.OwnershipTransferredEvent(groupId, currentOwnerId, newOwnerId));

            _logger.LogInformation("Ownership transferred from {OldOwner} to {NewOwner} in Server {GroupId}", currentOwnerId, newOwnerId, groupId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi chuyển quyền sở hữu Server {GroupId}", groupId);
            throw;
        }
    }

    /// <summary>
    /// Rời khỏi Server. Owner không thể rời nếu chưa chuyển nhượng quyền.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Kiểm tra role của User.
    /// 2. Nếu là Owner: Từ chối (phải Transfer trước).
    /// 3. Xóa record trong SQL.
    /// 4. Xóa role trong Redis Hash qua _permissionsCache.
    /// 5. Publish MemberLeftGroupEvent để các module khác (Room) dọn dẹp cache.
    /// </remarks>
    public async Task LeaveGroupAsync(Guid userId, Guid groupId)
    {
        var role = await _permissionsCache.GetMemberRoleAsync(groupId, userId);
        if (role == null) throw new KeyNotFoundException("Bạn không phải là thành viên của Server này.");

        if (role == GroupRole.Owner)
        {
            throw new InvalidOperationException("Chủ sở hữu không thể rời Server. Vui lòng chuyển nhượng quyền sở hữu hoặc giải tán Server.");
        }

        // Xóa trong SQL
        var member = await _context.GroupMembers.FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
        if (member != null)
        {
            _context.GroupMembers.Remove(member);
            await _context.SaveChangesAsync();
        }

        // Xóa trong Cache
        await _permissionsCache.RemoveUserFromGroupAsync(groupId, userId);

        // [EVENT] Bắn sự kiện để dọn dẹp cache Room và thông báo
        await _mediator.Publish(new Core.Events.MemberLeftGroupEvent(groupId, userId));

        _logger.LogInformation("User {UserId} left Server {GroupId}", userId, groupId);
    }

    /// <summary>
    /// Trục xuất thành viên khỏi Server.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Kiểm tra quyền người thực hiện (AdminId).
    /// 2. Admin chỉ kick được Member. Owner kick được tất cả trừ chính mình.
    /// 3. Xóa record trong SQL.
    /// 4. Xóa role trong Redis Hash.
    /// 5. Publish MemberKickedFromGroupEvent.
    /// </remarks>
    public async Task KickMemberAsync(Guid adminId, Guid groupId, Guid targetUserId)
    {
        if (adminId == targetUserId) throw new InvalidOperationException("Bạn không thể tự trục xuất chính mình.");

        var adminRole = await _permissionsCache.GetMemberRoleAsync(groupId, adminId);
        var targetRole = await _permissionsCache.GetMemberRoleAsync(groupId, targetUserId);

        if (adminRole == null) throw new UnauthorizedAccessException("Bạn không phải thành viên của Server này.");
        if (targetRole == null) throw new KeyNotFoundException("Thành viên mục tiêu không còn ở trong Server.");

        // Kiểm tra quyền kick
        bool canKick = false;
        if (adminRole == GroupRole.Owner) canKick = true;
        else if (adminRole == GroupRole.Admin && targetRole == GroupRole.Member) canKick = true;

        if (!canKick)
        {
            throw new UnauthorizedAccessException("Bạn không có đủ quyền hạn để trục xuất thành viên này.");
        }

        // Xóa trong SQL
        var member = await _context.GroupMembers.FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == targetUserId);
        if (member != null)
        {
            _context.GroupMembers.Remove(member);
            await _context.SaveChangesAsync();
        }

        // Xóa trong Cache
        await _permissionsCache.RemoveUserFromGroupAsync(groupId, targetUserId);

        // [EVENT] Bắn sự kiện để dọn dẹp cache Room và thông báo
        await _mediator.Publish(new Core.Events.MemberKickedFromGroupEvent(groupId, targetUserId, adminId));

        _logger.LogInformation("User {TargetUserId} was kicked from Server {GroupId} by {AdminId}", targetUserId, groupId, adminId);
    }

    /// <summary>
    /// Giải tán (Soft Delete) Server. Chỉ Owner mới có quyền.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Validate: Chỉ Owner mới có quyền.
    /// 2. SQL: Set DeletedAt = Now.
    /// 3. Lấy danh sách MemberIds (để báo cho Notification Module).
    /// 4. Redis: Xóa sạch Metadata, Permissions, Invite Mapping.
    /// 5. Publish GroupDeletedEvent.
    /// </remarks>
    public async Task SoftDeleteGroupAsync(Guid ownerId, Guid groupId)
    {
        var group = await _context.Groups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.DeletedAt == null);

        if (group == null) throw new KeyNotFoundException("Không tìm thấy Server hoặc Server đã bị giải tán.");

        if (group.OwnerId != ownerId)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ sở hữu mới có quyền giải tán Server.");
        }

        // 1. Lấy danh sách thành viên trước khi xóa (để gửi thông báo)
        var memberIds = await _context.GroupMembers
            .Where(gm => gm.GroupId == groupId)
            .Select(gm => gm.UserId)
            .ToListAsync();

        // 2. Soft Delete trong SQL
        group.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // 3. Xóa sạch Cache
        await _metadataCache.InvalidateGroupMetadataAsync(groupId);
        await _metadataCache.InvalidateInviteCodeMappingAsync(group.InviteCode);
        await _permissionsCache.InvalidateGroupMembersAsync(groupId);

        // 4. [EVENT] Bắn sự kiện giải tán
        await _mediator.Publish(new Core.Events.GroupDeletedEvent(groupId, ownerId, memberIds));

        _logger.LogWarning("Server {GroupId} was DELETED (Soft Delete) by Owner {OwnerId}", groupId, ownerId);
    }
}




