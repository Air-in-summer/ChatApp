using MediatR;
using Microsoft.EntityFrameworkCore;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Room.Handlers;

/// <summary>
/// Handler xử lý việc dọn dẹp toàn bộ Cache của các Room thuộc về một Group khi Group đó bị giải tán.
/// </summary>
public class GroupDeletedHandler : INotificationHandler<GroupDeletedEvent>
{
    private readonly AppDbContext _context;
    private readonly IRoomPermissionsCache _permissionsCache;
    private readonly ILogger<GroupDeletedHandler> _logger;

    public GroupDeletedHandler(
        AppDbContext context, 
        IRoomPermissionsCache permissionsCache, 
        ILogger<GroupDeletedHandler> logger)
    {
        _context = context;
        _permissionsCache = permissionsCache;
        _logger = logger;
    }

    public async Task Handle(GroupDeletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up all room caches for Deleted Group {GroupId}", notification.GroupId);

        // 1. Tìm tất cả các Room Id thuộc Group này (bao gồm cả các phòng đã bị soft-delete nếu cần, nhưng thường chỉ cần active rooms)
        var roomIds = await _context.Rooms
            .Where(r => r.GroupId == notification.GroupId)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (!roomIds.Any()) return;

        // 2. Xóa sập Cache của tất cả các phòng này
        var cleanupTasks = roomIds.Select(roomId => _permissionsCache.InvalidateRoomCacheAsync(roomId));
        await Task.WhenAll(cleanupTasks);

        _logger.LogInformation("Invalidated {Count} room caches for Deleted Group {GroupId}", roomIds.Count, notification.GroupId);
    }
}
