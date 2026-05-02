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
    private readonly IRoomPermissionsCache _permissionsCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    public SendMessageCommandHandler(
        IRoomPermissionsCache permissionsCache, 
        IConnectionMultiplexer redis,
        ILogger<SendMessageCommandHandler> logger)
    {
        _permissionsCache = permissionsCache;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Xử lý gửi tin nhắn cực nhanh, nhả luồng Websocket ngay lập tức.
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Gọi bộ đệm _permissionsCache để check quyền SISMEMBER siêu tốc.
    /// 2. Nếu User nằm trong Room -> Đóng gói payload.
    /// 3. Publish vào kênh "chat_messages_queue" của Redis.
    /// 4. Hết nhiệm vụ, kết thúc chu kỳ.
    /// </remarks>
    public async Task<bool> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Quyền Hạn
        bool isMember = await _permissionsCache.IsUserInRoomAsync(request.RoomId, request.SenderId);
        
        if (!isMember)
        {
            _logger.LogWarning("Bảo mật: User {UserId} cố gắng nhắn tin vào Room {RoomId} mà không có quyền.", request.SenderId, request.RoomId);
            return false; 
        }

        // 2. Chuyển đổi thành JSON siêu nhẹ
        // Lưu ý: Không tự cấp ID MongoDB ở đây. Việc đó giành cho BackgroundWorker để phân tải.
        var messagePayload = JsonSerializer.Serialize(request);

        // 3. Ném vào Redis Streams (Lưu trữ an toàn, chờ Worker xử lý và ACK)
        var db = _redis.GetDatabase();
        await db.StreamAddAsync("chat_messages_stream", "payload", messagePayload);

        return true;
    }
}
