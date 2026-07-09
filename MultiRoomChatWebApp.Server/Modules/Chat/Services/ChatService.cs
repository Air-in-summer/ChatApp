using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Shared.Exceptions;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public class ChatService : IChatService
{
    private const int MaxSearchPageSize = 50;
    private const int MaxSearchQueryLength = 100;

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
    /// 1. Lọc theo RoomId.
    /// 2. Nếu có beforeMessageId, lấy các message cũ hơn mốc đó.
    /// 3. Sắp xếp giảm dần theo ObjectId.
    /// 4. Giới hạn số lượng (limit)
    /// 5. Đảo ngược mảng kết quả để trả về đúng thứ tự thời gian (từ cũ đến mới) cho Frontend hiển thị.
    /// </remarks>
    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid roomId, string? beforeMessageId, int limit = 50)
    {
        var filterBuilder = Builders<Message>.Filter;
        var filter = filterBuilder.Eq(x => x.RoomId, roomId);

        if (!string.IsNullOrWhiteSpace(beforeMessageId))
        {
            filter &= filterBuilder.Lt(x => x.Id, beforeMessageId);
        }

        var messages = await _messagesCollection
            .Find(filter)
            .SortByDescending(x => x.Id)
            .Limit(limit)
            .ToListAsync();

            // Cursor-based: ObjectId lưu thời gian, tìm Id nhỏ hơn đồng nghĩa với tìm record cũ hơn

        // MongoDB trả về danh sách từ Mới nhất -> Cũ nhất.
        // Cần đảo ngược lại để UI render từ Cũ nhất -> Mới nhất (từ trên xuống dưới)
        messages.Reverse();
        await EnrichAttachmentUrlsAsync(messages);

        return messages;
    }

    /// <inheritdoc />
    public async Task<MessageContextResponseDto> GetMessageContextAsync(
        Guid roomId,
        string messageId,
        int before = 20,
        int after = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (roomId == Guid.Empty)
        {
            throw ApiException.BadRequest(
                "message_context_room_invalid",
                "Phong chat khong hop le.");
        }

        if (before < 0 || before > 50 || after < 0 || after > 50)
        {
            throw ApiException.BadRequest(
                "message_context_window_invalid",
                "So tin nhan truoc/sau phai nam trong khoang 0 den 50.");
        }

        if (string.IsNullOrWhiteSpace(messageId) ||
            !ObjectId.TryParse(messageId, out _))
        {
            throw ApiException.BadRequest(
                "message_id_invalid",
                "Ma tin nhan khong hop le.");
        }

        var filterBuilder = Builders<Message>.Filter;
        var roomFilter = filterBuilder.Eq(message => message.RoomId, roomId);
        var target = await _messagesCollection
            .Find(roomFilter & filterBuilder.Eq(message => message.Id, messageId))
            .FirstOrDefaultAsync(cancellationToken);

        if (target is null)
        {
            throw ApiException.NotFound(
                "message_not_found",
                "Khong tim thay tin nhan trong phong nay.");
        }

        var beforeCandidates = await _messagesCollection
            .Find(roomFilter & filterBuilder.Lt(message => message.Id, target.Id))
            .SortByDescending(message => message.Id)
            .Limit(before + 1)
            .ToListAsync(cancellationToken);
        var hasMoreBefore = beforeCandidates.Count > before;
        var beforeMessages = beforeCandidates
            .Take(before)
            .Reverse()
            .ToList();

        var afterCandidates = await _messagesCollection
            .Find(roomFilter & filterBuilder.Gt(message => message.Id, target.Id))
            .SortBy(message => message.Id)
            .Limit(after + 1)
            .ToListAsync(cancellationToken);
        var hasMoreAfter = afterCandidates.Count > after;
        var afterMessages = afterCandidates
            .Take(after)
            .ToList();

        // Context la timeline that quanh target, nen khong loc Message.Type hoac DeletedAt.
        var seenMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var contextMessages = beforeMessages
            .Concat([target])
            .Concat(afterMessages)
            .Where(message => seenMessageIds.Add(message.Id))
            .ToList();

        await EnrichAttachmentUrlsAsync(contextMessages);

        return new MessageContextResponseDto
        {
            TargetMessageId = target.Id,
            Messages = contextMessages,
            HasMoreBefore = hasMoreBefore,
            HasMoreAfter = hasMoreAfter,
            BeforeCursor = hasMoreBefore && beforeMessages.Count > 0
                ? beforeMessages[0].Id
                : null,
            AfterCursor = hasMoreAfter && afterMessages.Count > 0
                ? afterMessages[^1].Id
                : null
        };
    }

    /// <inheritdoc />
    public async Task<MessageSearchResponseDto> SearchMessagesAsync(
        Guid roomId,
        string query,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (roomId == Guid.Empty)
        {
            throw ApiException.BadRequest(
                "message_search_room_invalid",
                "Phong chat khong hop le.");
        }

        var searchQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(searchQuery))
        {
            throw ApiException.BadRequest(
                "message_search_query_empty",
                "Tu khoa tim kiem khong duoc de trong.");
        }

        if (searchQuery.Length > MaxSearchQueryLength)
        {
            throw ApiException.BadRequest(
                "message_search_query_too_long",
                $"Tu khoa tim kiem khong duoc vuot qua {MaxSearchQueryLength} ky tu.");
        }

        if (page < 1)
        {
            throw ApiException.BadRequest(
                "message_search_page_invalid",
                "Trang tim kiem phai lon hon hoac bang 1.");
        }

        if (pageSize < 1 || pageSize > MaxSearchPageSize)
        {
            throw ApiException.BadRequest(
                "message_search_page_size_invalid",
                $"So ket qua moi trang phai tu 1 den {MaxSearchPageSize}.");
        }

        var filterBuilder = Builders<Message>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(message => message.RoomId, roomId),
            filterBuilder.Eq(message => message.DeletedAt, null),
            filterBuilder.Ne(message => message.Content, string.Empty),
            filterBuilder.Regex(
                message => message.Content,
                new BsonRegularExpression(Regex.Escape(searchQuery), "i")));

        var totalCount = await _messagesCollection
            .CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var skip = (page - 1L) * pageSize;
        IReadOnlyList<MessageSearchResultDto> items = Array.Empty<MessageSearchResultDto>();

        if (skip <= int.MaxValue && skip < totalCount)
        {
            items = await _messagesCollection
                .Find(filter)
                .SortByDescending(message => message.Id)
                .Skip((int)skip)
                .Limit(pageSize)
                .Project(message => new MessageSearchResultDto
                {
                    MessageId = message.Id,
                    RoomId = message.RoomId,
                    SenderId = message.SenderId,
                    Content = message.Content,
                    CreatedAt = message.CreatedAt
                })
                .ToListAsync(cancellationToken);
        }

        return new MessageSearchResponseDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = ToBoundedInt(totalCount),
            TotalPages = totalCount == 0
                ? 0
                : ToBoundedInt(((totalCount - 1) / pageSize) + 1)
        };
    }

    public async Task<Dictionary<Guid, (Message? LastMessage, int UnreadCount, string? LastReadMessageId)>> GetRoomOverviewsAsync(Guid userId, List<Guid> roomIds)
    {
        var result = new Dictionary<Guid, (Message?, int, string?)>();
        if (roomIds == null || !roomIds.Any()) return result;

        // PostgreSQL là mốc bền vững; Redis ghi đè bằng trạng thái mới chưa được worker flush.
        var persistedReceipts = await _dbContext.ReadReceipts
            .AsNoTracking()
            .Where(r => r.UserId == userId && roomIds.Contains(r.RoomId))
            .Select(r => new
            {
                r.RoomId,
                r.LastReadMessageId
            })
            .ToListAsync();

        var readReceipts = persistedReceipts.ToDictionary(
            receipt => receipt.RoomId,
            receipt => receipt.LastReadMessageId);

        var redisDb = _redis.GetDatabase();
        var redisReceiptTasks = roomIds.Select(async roomId =>
        {
            var value = await redisDb.HashGetAsync(
                $"Room:{roomId}:ReadReceipts",
                userId.ToString());
            return (RoomId: roomId, Value: value);
        });

        var redisReceipts = await Task.WhenAll(redisReceiptTasks);
        foreach (var redisReceipt in redisReceipts.Where(receipt => receipt.Value.HasValue))
        {
            readReceipts[redisReceipt.RoomId] = redisReceipt.Value.ToString();
        }

        var tasks = roomIds.Select(async roomId =>
        {
            var lastMessage = await _messagesCollection
                .Find(m => m.RoomId == roomId)
                .SortByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            int unreadCount = 0;
            if (lastMessage != null)
            {
                readReceipts.TryGetValue(roomId, out var lastReadMessageId);
                if (!string.IsNullOrEmpty(lastReadMessageId))
                {
                    // Receipt cũ chưa có sequence tiếp tục dùng ObjectId đến khi được backfill.
                    var filter = Builders<Message>.Filter.And(
                        Builders<Message>.Filter.Eq(m => m.RoomId, roomId),
                        Builders<Message>.Filter.Ne(m => m.SenderId, userId),
                        Builders<Message>.Filter.Gt(
                            m => m.Id,
                            lastReadMessageId)
                    );
                    unreadCount = ToUnreadCount(
                        await _messagesCollection.CountDocumentsAsync(filter));
                }
                else
                {
                    var filter = Builders<Message>.Filter.And(
                        Builders<Message>.Filter.Eq(m => m.RoomId, roomId),
                        Builders<Message>.Filter.Ne(m => m.SenderId, userId)
                    );
                    unreadCount = ToUnreadCount(
                        await _messagesCollection.CountDocumentsAsync(filter));
                }
            }

            readReceipts.TryGetValue(roomId, out var lastReadIdForRoom);
            return (
                roomId: roomId,
                lastMessage: lastMessage,
                unreadCount: unreadCount,
                lastReadIdForRoom);
        });

        var overviews = await Task.WhenAll(tasks);
        foreach (var o in overviews)
        {
            result[o.roomId] = (o.lastMessage, o.unreadCount, o.lastReadIdForRoom);
        }

        return result;
    }

    private static int ToBoundedInt(long count) =>
        count >= int.MaxValue ? int.MaxValue : (int)count;

    private static int ToUnreadCount(long count) => ToBoundedInt(count);

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
