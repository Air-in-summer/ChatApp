using MediatR;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using System.Text.Json;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Handlers;

/// <summary>
/// Luồng xử lý cho SendMessageCommand. 
/// Tuyệt đối KHÔNG chạm vào DB SQL hay MongoDB ở đây. Chỉ check quyền (Redis) -> Ném queue (Redis).
/// </summary>
public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, bool>
{
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly Modules.Group.Core.Interfaces.IGroupPermissionsCache _groupPermissionsCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    public SendMessageCommandHandler(
        IRoomPermissionsCache roomPermissionsCache, 
        IRoomMetadataCache roomMetadataCache,
        Modules.Group.Core.Interfaces.IGroupPermissionsCache groupPermissionsCache,
        IConnectionMultiplexer redis,
        ILogger<SendMessageCommandHandler> logger)
    {
        _roomPermissionsCache = roomPermissionsCache;
        _roomMetadataCache = roomMetadataCache;
        _groupPermissionsCache = groupPermissionsCache;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Xử lý gửi tin nhắn với cơ chế Phân tầng Bảo mật (Hierarchical Auth).
    /// </summary>
    public async Task<bool> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Lấy Metadata của Phòng
        var roomMeta = await _roomMetadataCache.GetRoomMetadataAsync(request.RoomId);
        
        if (roomMeta == null)
        {
            _logger.LogWarning("Phòng {RoomId} không tồn tại hoặc đã bị xóa.", request.RoomId);
            return false;
        }

        // 2. DISPATCHER: Quyết định cách check quyền
        bool isAuthorized = false;

        if (roomMeta.Value.GroupId.HasValue && !roomMeta.Value.IsPrivate)
        {
            // TẦNG 1: PHÒNG PUBLIC TRONG GROUP -> Check Group Cache (Tránh nhồi 500 member vào Room Cache)
            isAuthorized = await _groupPermissionsCache.IsUserInGroupAsync(roomMeta.Value.GroupId.Value, request.SenderId);
        }
        else
        {
            // TẦNG 2: PHÒNG PRIVATE HOẶC DM -> Check Room Cache (Tối ưu cho phòng ít người)
            isAuthorized = await _roomPermissionsCache.IsUserInRoomAsync(request.RoomId, request.SenderId);
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Bảo mật: User {UserId} cố gắng nhắn tin vào Room {RoomId} mà không có quyền.", request.SenderId, request.RoomId);
            return false; 
        }

        // 3. Đóng gói payload
        var messagePayload = JsonSerializer.Serialize(request);

        // 4. Ném vào Redis Streams chờ Worker xử lý
        var db = _redis.GetDatabase();
        await db.StreamAddAsync("chat_messages_stream", "payload", messagePayload);

        return true;
    }
}
