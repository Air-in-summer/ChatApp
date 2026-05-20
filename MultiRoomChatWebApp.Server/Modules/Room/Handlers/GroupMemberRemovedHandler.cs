using MediatR;
using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Room.Handlers;

/// <summary>
/// Handler xử lý việc dọn dẹp Cache bảo mật trong Room Module khi một thành viên rời khỏi hoặc bị kick khỏi Group.
/// </summary>
public class GroupMemberRemovedHandler : 
    INotificationHandler<MemberLeftGroupEvent>, 
    INotificationHandler<MemberKickedFromGroupEvent>
{
    private readonly AppDbContext _context;
    private readonly IRoomPermissionsCache _permissionsCache;
    private readonly ILogger<GroupMemberRemovedHandler> _logger;

    public GroupMemberRemovedHandler(
        AppDbContext context, 
        IRoomPermissionsCache permissionsCache, 
        ILogger<GroupMemberRemovedHandler> logger)
    {
        _context = context;
        _permissionsCache = permissionsCache;
        _logger = logger;
    }

    /// <summary>
    /// Xử lý khi member tự rời Group
    /// </summary>
    public async Task Handle(MemberLeftGroupEvent notification, CancellationToken cancellationToken)
    {
        await CleanupRoomDataAsync(notification.GroupId, notification.UserId);
    }

    /// <summary>
    /// Xử lý khi member bị kick khỏi Group
    /// </summary>
    public async Task Handle(MemberKickedFromGroupEvent notification, CancellationToken cancellationToken)
    {
        await CleanupRoomDataAsync(notification.GroupId, notification.KickedUserId);
    }

    /// <summary>
    /// Logic dùng chung: Dọn dẹp cả SQL và Cache cho tất cả Room thuộc Group khi thành viên bị loại bỏ.
    /// </summary>
    private async Task CleanupRoomDataAsync(Guid groupId, Guid userId)
    {
        _logger.LogInformation("Cleaning up room data (SQL & Cache) for User {UserId} in Group {GroupId}", userId, groupId);

        // 1. Tìm tất cả các RoomMembers của User này trong Group
        // Lấy danh sách này trước để vừa xóa SQL vừa có RoomId dọn Cache, tránh query 2 lần.
        var roomMembers = await _context.RoomMembers
            .Where(rm => rm.UserId == userId && rm.Room.GroupId == groupId)
            .ToListAsync();

        if (!roomMembers.Any())
        {
            _logger.LogInformation("No room memberships found to clean up for User {UserId} in Group {GroupId}", userId, groupId);
            return;
        }

        // Lấy danh sách Id các phòng để dọn Cache ở bước sau
        var roomIds = roomMembers.Select(rm => rm.RoomId).ToList();

        // 2. Xử lý SQL: Xóa sạch các bản ghi RoomMembers
        _logger.LogInformation("Removing {Count} RoomMember SQL records for User {UserId} in Group {GroupId}", roomMembers.Count, userId, groupId);
        _context.RoomMembers.RemoveRange(roomMembers);
        await _context.SaveChangesAsync();

        // 3. Xử lý Cache: Dọn dẹp song song cho tất cả các phòng User đã tham gia
        var cleanupTasks = roomIds.Select(roomId => _permissionsCache.RemoveUserFromRoomAsync(roomId, userId));
        await Task.WhenAll(cleanupTasks);

        _logger.LogInformation("Finished cleaning up data for {Count} rooms in Group {GroupId}", roomIds.Count, groupId);
    }
}
