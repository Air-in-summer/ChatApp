using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Worker chạy ngầm suốt vòng đời ứng dụng.
/// Nhiệm vụ DUY NHẤT: Lắng nghe Redis Pub/Sub → Lưu tin nhắn vào MongoDB → Broadcast SignalR.
/// (ReceiveMessage tới tất cả thành viên + MessageStatusUpdated tới người gửi)
/// </summary>
/// <remarks>
/// Broadcast bằng Clients.Users() thay vì Clients.Group().
/// Lý do: Khi A tạo phòng mới với B, B chưa JoinRoom vào SignalR Group nên
/// Clients.Group() không gửi được tới B. Clients.Users() tự động ánh xạ
/// UserId → ConnectionId qua ClaimTypes.NameIdentifier, hoạt động ngay lập tức
/// mà không cần B phải JoinRoom trước.
/// </remarks>
public class MessagePersistenceWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMongoClient _mongoClient;
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessagePersistenceWorker> _logger;

    public MessagePersistenceWorker(
        IConnectionMultiplexer redis,
        IMongoClient mongoClient,
        IHubContext<ChatHub, IChatClient> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<MessagePersistenceWorker> logger)
    {
        _redis = redis;
        _mongoClient = mongoClient;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lấy Database và Collection MongoDB
        var db = _mongoClient.GetDatabase("ChatAppDB_Mongo");
        var messagesCollection = db.GetCollection<Message>("messages");

        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal("chat_messages_queue");

        _logger.LogInformation("🚀 MessagePersistenceWorker đã khởi động. Lắng nghe kênh Redis: {Channel}", channel);

        // ---- LUỒNG 1: Lắng nghe Redis Pub/Sub → Lưu Mongo → Bắn SignalR ----
        await subscriber.SubscribeAsync(channel, async (ch, messagePayload) =>
        {
            try
            {
                var command = JsonSerializer.Deserialize<SendMessageCommand>(messagePayload!);
                if (command == null) return;

                // 1. Dựng lại thực thể Message để nhét vô DB
                var newMessage = new Message
                {
                    RoomId = command.RoomId,
                    SenderId = command.SenderId,
                    Content = command.Content,
                    Attachments = command.Attachments,
                    CreatedAt = DateTime.UtcNow,
                    Status = Core.Enums.MessageStatus.Sent,
                    Type = Core.Enums.MessageType.Text
                };

                // 2. Insert siêu tốc vào MongoDB
                await messagesCollection.InsertOneAsync(newMessage, cancellationToken: stoppingToken);

                // 3. Query danh sách thành viên phòng từ PostgreSQL
                // Tạo scope mới vì BackgroundService là Singleton, không thể inject Scoped DbContext
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var memberIds = await dbContext.RoomMembers
                    .AsNoTracking()
                    .Where(rm => rm.RoomId == command.RoomId)
                    .Select(rm => rm.UserId.ToString())
                    .ToListAsync(stoppingToken);

                // 4. Phân phát tin nhắn qua Websocket tới TẤT CẢ thành viên (không phụ thuộc Group)
                await _hubContext.Clients.Users(memberIds).ReceiveMessage(newMessage);

                // 5. Gọi lại người GỬI để cập nhật trạng thái tin tạm → Sent
                // Chỉ gọi nếu TempId có giá trị (tức là gửi từ FE, không phải API test)
                if (!string.IsNullOrEmpty(command.TempId))
                {
                    await _hubContext.Clients.User(command.SenderId.ToString())
                        .MessageStatusUpdated(command.TempId, newMessage.Id, "Sent");
                }

                _logger.LogInformation("✅ Worker: Đã lưu MongoDB và Broadcast tin nhắn {MessageId} tới {MemberCount} thành viên",
                    newMessage.Id, memberIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nghiêm trọng khi xử lý Background Message từ Redis.");
            }
        });

        // Vòng lặp vô tận giữ cho Worker sống sót cùng Kestrel
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
        }
    }
}

