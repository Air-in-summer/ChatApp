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

        var dbRedis = _redis.GetDatabase();
        var streamName = "chat_messages_stream";
        var groupName = "persistence_worker_group";
        var consumerName = "worker_1"; // Có thể sinh ID ngẫu nhiên nếu có nhiều instance

        // 1. Khởi tạo Consumer Group (nếu chưa có)
        try
        {
            // "0-0" nghĩa là group này sẽ tiêu thụ toàn bộ tin nhắn tồn đọng (nếu có) từ đầu stream
            await dbRedis.StreamCreateConsumerGroupAsync(streamName, groupName, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Consumer Group đã tồn tại, không sao cả
        }

        _logger.LogInformation("🚀 MessagePersistenceWorker đã khởi động. Lắng nghe Redis Stream: {Stream}", streamName);

        // 2. Vòng lặp tiêu thụ tin nhắn
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // [CHIẾN LƯỢC PHỤC HỒI]: 
                // Ưu tiên đọc các tin nhắn đang "Pending" (ID: "0") của chính consumer này 
                // (Đây là những tin đã lấy ra nhưng chưa kịp ACK do Worker bị sập/restart).
                var messages = await dbRedis.StreamReadGroupAsync(streamName, groupName, consumerName, "0", count: 10);

                // Nếu không còn tin Pending, mới đọc tiếp tin mới tinh (ID: ">")
                if (messages.Length == 0)
                {
                    messages = await dbRedis.StreamReadGroupAsync(streamName, groupName, consumerName, ">", count: 10);
                }

                if (messages.Length == 0)
                {
                    // Tránh nghẽn CPU nếu không có tin nhắn
                    await Task.Delay(50, stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    var payload = message.Values.FirstOrDefault(v => v.Name == "payload").Value;
                    if (payload.IsNullOrEmpty) continue;

                    var command = JsonSerializer.Deserialize<SendMessageCommand>(payload.ToString());
                    if (command == null) continue;

                    // 1. Dựng lại thực thể Message và TỰ TẠO ID TRƯỚC để lấy ID đi Broadcast ngay
                    var newMessage = new Message
                    {
                        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        RoomId = command.RoomId,
                        SenderId = command.SenderId,
                        Content = command.Content,
                        Attachments = command.Attachments,
                        CreatedAt = DateTime.UtcNow,
                        Status = Core.Enums.MessageStatus.Sent,
                        Type = Core.Enums.MessageType.Text
                    };

                    // Hàm cục bộ phụ trách việc Query Postgres và Bắn SignalR
                    async Task BroadcastToMembersAsync()
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        var memberIds = await dbContext.RoomMembers
                            .AsNoTracking()
                            .Where(rm => rm.RoomId == command.RoomId)
                            .Select(rm => rm.UserId.ToString())
                            .ToListAsync(stoppingToken);

                        // Phân phát tin nhắn qua Websocket tới TẤT CẢ thành viên
                        await _hubContext.Clients.Users(memberIds).ReceiveMessage(newMessage);

                        // Gọi lại người GỬI để cập nhật trạng thái
                        if (!string.IsNullOrEmpty(command.TempId))
                        {
                            await _hubContext.Clients.User(command.SenderId.ToString())
                                .MessageStatusUpdated(command.TempId, newMessage.Id, "Sent");
                        }

                        _logger.LogInformation("✅ Worker: Đã Broadcast tin nhắn {MessageId} tới {MemberCount} thành viên",
                            newMessage.Id, memberIds.Count);
                    }

                    // 2. ÉP CHẠY SONG SONG (PARALLEL): MongoDB Insert và SignalR Broadcast chạy đua cùng lúc!
                    var insertMongoTask = messagesCollection.InsertOneAsync(newMessage, cancellationToken: stoppingToken);
                    var broadcastTask = BroadcastToMembersAsync();
                    
                    await Task.WhenAll(insertMongoTask, broadcastTask);

                    // 3. XÁC NHẬN (ACK) VÀ XÓA KHỎI STREAM
                    // Chỉ chạy đến đây khi CẢ 2 việc trên đều đã báo thành công
                    await dbRedis.StreamAcknowledgeAsync(streamName, groupName, message.Id);
                    await dbRedis.StreamDeleteAsync(streamName, new[] { message.Id });

                    //_logger.LogInformation("✅ Worker: Đã xử lý, ACK và XÓA tin nhắn {MessageId} tới {MemberCount} thành viên",
                    //    newMessage.Id, memberIds.Count);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nghiêm trọng khi xử lý Stream Message. Thử lại sau 5s.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}

