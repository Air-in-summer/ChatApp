using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public class ChatService : IChatService
{
    private readonly IMongoCollection<Message> _messagesCollection;
    private readonly AppDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly MediaStorageOptions _mediaOptions;

    public ChatService(
        IMongoDatabase mongoDatabase,
        AppDbContext dbContext,
        IConnectionMultiplexer redis,
        IMediaStorageService mediaStorageService,
        IOptions<MediaStorageOptions> mediaOptions)
    {
        _messagesCollection = mongoDatabase.GetCollection<Message>("messages");
        _dbContext = dbContext;
        _redis = redis;
        _mediaStorageService = mediaStorageService;
        _mediaOptions = mediaOptions.Value;
    }

    /// <summary>
    /// Lấy danh sách tin nhắn của một phòng chat
    /// </summary>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Lọc theo RoomId
    /// 2. Nếu có cursor, lọc thêm điều kiện Id < cursor (lấy tin nhắn cũ hơn)
    /// 3. Sắp xếp giảm dần theo Id (từ mới đến cũ)
    /// 4. Giới hạn số lượng (limit)
    /// 5. Đảo ngược mảng kết quả để trả về đúng thứ tự thời gian (từ cũ đến mới) cho Frontend hiển thị.
    /// </remarks>
    public async Task<IEnumerable<Message>> GetMessagesAsync(Guid roomId, string? cursor, int limit = 50)
    {
        var filterBuilder = Builders<Message>.Filter;
        var filter = filterBuilder.Eq(x => x.RoomId, roomId);

        if (!string.IsNullOrEmpty(cursor))
        {
            // Cursor-based: ObjectId lưu thời gian, tìm Id nhỏ hơn đồng nghĩa với tìm record cũ hơn
            filter &= filterBuilder.Lt(x => x.Id, cursor);
        }

        // Tối ưu hoá với Compound Index { room_id: 1, _id: -1 } đã tạo
        var messages = await _messagesCollection.Find(filter)
            .SortByDescending(x => x.Id)
            .Limit(limit)
            .ToListAsync();

        // MongoDB trả về danh sách từ Mới nhất -> Cũ nhất.
        // Cần đảo ngược lại để UI render từ Cũ nhất -> Mới nhất (từ trên xuống dưới)
        messages.Reverse();
        await EnrichAttachmentUrlsAsync(messages);

        return messages;
    }

    public async Task<Dictionary<Guid, (Message? LastMessage, int UnreadCount, string? LastReadMessageId)>> GetRoomOverviewsAsync(Guid userId, List<Guid> roomIds)
    {
        var result = new Dictionary<Guid, (Message?, int, string?)>();
        if (roomIds == null || !roomIds.Any()) return result;

        // 1. Lấy ReadReceipts từ PostgreSQL làm base
        var readReceipts = await _dbContext.ReadReceipts
            .Where(r => r.UserId == userId && roomIds.Contains(r.RoomId))
            .ToDictionaryAsync(r => r.RoomId, r => r.LastReadMessageId);

        // Đọc đè từ Redis (vì Redis chứa state mới nhất, chưa flush xuống DB)
        var redisDb = _redis.GetDatabase();
        foreach (var roomId in roomIds)
        {
            var key = $"Room:{roomId}:ReadReceipts";
            var redisVal = await redisDb.HashGetAsync(key, userId.ToString());
            if (redisVal.HasValue)
            {
                readReceipts[roomId] = redisVal.ToString();
            }
        }

        // 2. Query MongoDB song song cho mỗi phòng
        var tasks = roomIds.Select(async roomId =>
        {
            // Lấy tin nhắn cuối cùng (mới nhất)
            var lastMessage = await _messagesCollection
                .Find(m => m.RoomId == roomId)
                .SortByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            int unreadCount = 0;
            if (lastMessage != null)
            {
                // Nếu chưa có read receipt, tất cả tin nhắn đều là unread?
                // Thường thì chỉ đếm những tin mới nhất từ khi join. Tạm thời đếm tất cả tin có Id > LastReadMessageId
                if (readReceipts.TryGetValue(roomId, out var lastReadId) && !string.IsNullOrEmpty(lastReadId))
                {
                    var filter = Builders<Message>.Filter.And(
                        Builders<Message>.Filter.Eq(m => m.RoomId, roomId),
                        Builders<Message>.Filter.Ne(m => m.SenderId, userId),
                        Builders<Message>.Filter.Gt(m => m.Id, lastReadId)
                    );
                    unreadCount = (int)await _messagesCollection.CountDocumentsAsync(filter);
                }
                else
                {
                    // Nếu chưa từng đọc, đếm các tin do người khác gửi (tối đa 50)
                    var filter = Builders<Message>.Filter.And(
                        Builders<Message>.Filter.Eq(m => m.RoomId, roomId),
                        Builders<Message>.Filter.Ne(m => m.SenderId, userId)
                    );
                    unreadCount = (int)await _messagesCollection.Find(filter).Limit(50).CountDocumentsAsync();
                }
            }

            readReceipts.TryGetValue(roomId, out var lastReadIdForRoom);
            return (roomId, lastMessage, unreadCount, lastReadIdForRoom);
        });

        var overviews = await Task.WhenAll(tasks);
        foreach (var o in overviews)
        {
            result[o.roomId] = (o.lastMessage, o.unreadCount, o.lastReadIdForRoom);
        }

        return result;
    }

    private async Task EnrichAttachmentUrlsAsync(List<Message> messages)
    {
        var mediaIds = messages
            .SelectMany(message => message.Attachments ?? [])
            .Where(attachment => attachment.MediaId.HasValue)
            .Select(attachment => attachment.MediaId!.Value)
            .Distinct()
            .ToList();

        if (mediaIds.Count == 0)
            return;

        var mediaAssets = await _dbContext.MediaAssets
            .AsNoTracking()
            .Where(asset => mediaIds.Contains(asset.Id) && asset.DeletedAt == null)
            .ToDictionaryAsync(asset => asset.Id);

        var ttl = TimeSpan.FromMinutes(Math.Max(1, _mediaOptions.SignedUrlMinutes));
        var expiresAt = DateTime.UtcNow.Add(ttl);

        foreach (var message in messages)
        {
            if (message.Attachments == null)
                continue;

            foreach (var attachment in message.Attachments)
            {
                if (!attachment.MediaId.HasValue ||
                    !mediaAssets.TryGetValue(attachment.MediaId.Value, out var mediaAsset))
                {
                    continue;
                }

                if (mediaAsset.Status != MediaAssetStatus.Attached ||
                    mediaAsset.RoomId != message.RoomId)
                {
                    continue;
                }

                if (mediaAsset.AccessLevel == MediaAccessLevel.PublicRead &&
                    !string.IsNullOrWhiteSpace(mediaAsset.PublicUrl))
                {
                    attachment.Url = mediaAsset.PublicUrl;
                    attachment.ExpiresAt = null;
                    continue;
                }

                attachment.Url = _mediaStorageService.CreatePresignedGetUrl(
                    mediaAsset.BucketName,
                    mediaAsset.StorageKey,
                    ttl);
                attachment.ExpiresAt = expiresAt;
            }
        }
    }
}
