using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Enums;
using GroupEntity = MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.Group;
using GroupMemberEntity = MultiRoomChatWebApp.Server.Modules.Group.Core.Entities.GroupMember;
using Microsoft.Extensions.Logging;

using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Group.Services;

public class GroupService : IGroupService
{
    private readonly AppDbContext _context;
    private readonly IRoomService _roomService;
    private readonly ILogger<GroupService> _logger;

    public GroupService(AppDbContext context, IRoomService roomService, ILogger<GroupService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // 4. Đẩy Server vào DbContext
        _context.Groups.Add(newGroup);
        _context.GroupMembers.Add(groupMember);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("User {UserId} created a new Server '{GroupName}' with Id {GroupId}", userId, newGroup.Name, newGroup.Id);

            // 5. Khởi tạo Channel mặc định (#general) qua RoomService
            var defaultRoom = await _roomService.CreateGroupRoomAsync(
                name: "general",
                type: MultiRoomChatWebApp.Server.Modules.Room.Core.Enums.RoomType.Text,
                isPrivate: false,
                createdBy: userId,
                groupId: newGroup.Id
            );
            _logger.LogInformation("Created default channel {RoomId} for Server {GroupId}", defaultRoom.Id, newGroup.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu Server mới vào cơ sở dữ liệu");
            throw; 
        }

        // 6. Trả về kết quả
        return new GroupDto
        {
            Id = newGroup.Id,
            Name = newGroup.Name,
            Description = newGroup.Description,
            IconUrl = newGroup.IconUrl,
            InviteCode = newGroup.InviteCode,
            OwnerId = newGroup.OwnerId,
            CreatedAt = newGroup.CreatedAt
        };
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
        // 1. Kiểm tra sự tồn tại và quyền hạn
        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

        if (member == null)
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên của Server này.");
        }

        if (member.Role != GroupRole.Owner && member.Role != GroupRole.Admin)
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
            
            _logger.LogInformation("User {UserId} created a new Channel '{ChannelName}' in Server {GroupId}", userId, newRoom.Name, groupId);
            return newRoom.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu Channel mới vào Server {GroupId}", groupId);
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
        // 1. Tìm Server theo InviteCode
        var group = await _context.Groups
            .FirstOrDefaultAsync(g => g.InviteCode == inviteCode);

        if (group == null)
        {
            throw new KeyNotFoundException("Mã mời không hợp lệ hoặc Server không còn tồn tại.");
        }

        // 2. Kiểm tra xem User đã ở trong Server chưa
        var existingMember = await _context.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == group.Id && gm.UserId == userId);

        if (existingMember != null)
        {
            // User đã là thành viên, không cần làm gì thêm, chỉ trả về thông tin Server
            _logger.LogInformation("User {UserId} is already a member of Server {GroupId}", userId, group.Id);
        }
        else
        {
            // 3. Khởi tạo bản ghi Thành viên
            var newMember = new GroupMemberEntity
            {
                GroupId = group.Id,
                UserId = userId,
                Role = GroupRole.Member
            };

            _context.GroupMembers.Add(newMember);

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("User {UserId} joined Server {GroupId} via InviteCode {InviteCode}", userId, group.Id, inviteCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm User {UserId} vào Server {GroupId}", userId, group.Id);
                throw;
            }
        }

        // 4. Trả về thông tin Server
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
}
