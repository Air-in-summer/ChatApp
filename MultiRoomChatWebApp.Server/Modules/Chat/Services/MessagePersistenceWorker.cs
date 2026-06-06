using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Infrastructure.Database;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Group.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Options;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

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
        var mongoDb = _mongoClient.GetDatabase("ChatAppDB_Mongo");
        var messagesCollection = mongoDb.GetCollection<Message>("messages");

        var redisDb = _redis.GetDatabase();
        var streamName = "chat_messages_stream";
        var groupName = "persistence_worker_group";
        var consumerName = "worker_1";

        try
        {
            await redisDb.StreamCreateConsumerGroupAsync(streamName, groupName, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Consumer group da ton tai.
        }

        _logger.LogInformation("MessagePersistenceWorker da khoi dong. Stream={Stream}", streamName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var streamEntries = await redisDb.StreamReadGroupAsync(streamName, groupName, consumerName, "0", count: 10);
                if (streamEntries.Length == 0)
                {
                    streamEntries = await redisDb.StreamReadGroupAsync(streamName, groupName, consumerName, ">", count: 10);
                }

                if (streamEntries.Length == 0)
                {
                    await Task.Delay(50, stoppingToken);
                    continue;
                }

                foreach (var streamEntry in streamEntries)
                {
                    var payload = streamEntry.Values.FirstOrDefault(value => value.Name == "payload").Value;
                    if (payload.IsNullOrEmpty)
                    {
                        await AcknowledgeAndDeleteAsync(redisDb, streamName, groupName, streamEntry.Id);
                        continue;
                    }

                    var command = JsonSerializer.Deserialize<SendMessageCommand>(payload.ToString());
                    if (command == null)
                    {
                        await AcknowledgeAndDeleteAsync(redisDb, streamName, groupName, streamEntry.Id);
                        continue;
                    }

                    command.Content = command.Content?.Trim() ?? string.Empty;
                    command.MediaIds = (command.MediaIds ?? [])
                        .Where(mediaId => mediaId != Guid.Empty)
                        .Distinct()
                        .ToList();

                    if (string.IsNullOrWhiteSpace(command.Content) && command.MediaIds.Count == 0)
                    {
                        _logger.LogWarning(
                            "Worker: Bo qua message rong cua User {UserId} vao Room {RoomId}.",
                            command.SenderId,
                            command.RoomId);
                        await NotifyMessageFailedAsync(command);
                        await AcknowledgeAndDeleteAsync(redisDb, streamName, groupName, streamEntry.Id);
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var mediaStorageService = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
                    var mediaOptions = scope.ServiceProvider.GetRequiredService<IOptions<MediaStorageOptions>>().Value;
                    var roomMetadataCache = scope.ServiceProvider.GetRequiredService<IRoomMetadataCache>();
                    var roomPermissionsCache = scope.ServiceProvider.GetRequiredService<IRoomPermissionsCache>();
                    var groupPermissionsCache = scope.ServiceProvider.GetRequiredService<IGroupPermissionsCache>();

                    var roomMetadata = await roomMetadataCache.GetRoomMetadataAsync(command.RoomId);
                    if (roomMetadata == null)
                    {
                        _logger.LogWarning(
                            "Worker: Khong tim thay metadata Room {RoomId}, bo qua message tu User {UserId}.",
                            command.RoomId,
                            command.SenderId);
                        await NotifyMessageFailedAsync(command);
                        await AcknowledgeAndDeleteAsync(redisDb, streamName, groupName, streamEntry.Id);
                        continue;
                    }

                    var pendingMediaAssets = await LoadValidPendingMediaAssetsAsync(
                        appDbContext,
                        command,
                        stoppingToken);

                    if (pendingMediaAssets == null)
                    {
                        await NotifyMessageFailedAsync(command);
                        await AcknowledgeAndDeleteAsync(redisDb, streamName, groupName, streamEntry.Id);
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    var storedAttachments = BuildAttachmentSnapshots(
                        pendingMediaAssets,
                        mediaStorageService,
                        mediaOptions,
                        now,
                        includeAccessUrls: false);

                    var newMessage = new Message
                    {
                        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        RoomId = command.RoomId,
                        SenderId = command.SenderId,
                        Content = command.Content,
                        Attachments = storedAttachments.Count == 0 ? null : storedAttachments,
                        CreatedAt = now,
                        Status = MessageStatus.Sent,
                        Type = DetermineMessageType(command.Content, storedAttachments)
                    };

                    await messagesCollection.InsertOneAsync(newMessage, cancellationToken: stoppingToken);

                    if (pendingMediaAssets.Count > 0)
                    {
                        foreach (var mediaAsset in pendingMediaAssets)
                        {
                            mediaAsset.Status = MediaAssetStatus.Attached;
                            mediaAsset.RoomId = command.RoomId;
                            mediaAsset.MessageId = newMessage.Id;
                            mediaAsset.AttachedAt = now;
                        }

                        await appDbContext.SaveChangesAsync(stoppingToken);
                    }

                    var deliveryMessage = CloneMessageForDelivery(
                        newMessage,
                        BuildAttachmentSnapshots(
                            pendingMediaAssets,
                            mediaStorageService,
                            mediaOptions,
                            DateTime.UtcNow,
                            includeAccessUrls: true));

                    var memberIds = roomMetadata.Value.Type == RoomType.Text &&
                                    roomMetadata.Value.GroupId.HasValue &&
                                    !roomMetadata.Value.IsPrivate
                        ? (await groupPermissionsCache.GetGroupMemberRolesAsync(roomMetadata.Value.GroupId.Value))
                            .Keys
                            .Select(userId => userId.ToString())
                            .ToList()
                        : (await roomPermissionsCache.GetRoomMemberIdsAsync(command.RoomId))
                            .Select(userId => userId.ToString())
                            .ToList();

                    await _hubContext.Clients.Users(memberIds).ReceiveMessage(deliveryMessage);

                    if (!string.IsNullOrEmpty(command.TempId))
                    {
                        await _hubContext.Clients.User(command.SenderId.ToString())
                            .MessageStatusUpdated(command.TempId, newMessage.Id, "Sent");
                    }

                    _logger.LogInformation(
                        "Worker: Da broadcast message {MessageId} toi {MemberCount} thanh vien; AttachmentCount={AttachmentCount}",
                        newMessage.Id,
                        memberIds.Count,
                        storedAttachments.Count);

                    await AcknowledgeAndDeleteAsync(redisDb, streamName, groupName, streamEntry.Id);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loi nghiem trong khi xu ly Stream Message. Thu lai sau 5s.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<List<MediaAsset>?> LoadValidPendingMediaAssetsAsync(
        AppDbContext appDbContext,
        SendMessageCommand command,
        CancellationToken cancellationToken)
    {
        if (command.MediaIds.Count == 0)
            return [];

        var mediaAssets = await appDbContext.MediaAssets
            .Where(asset => command.MediaIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);

        if (mediaAssets.Count != command.MediaIds.Count)
        {
            _logger.LogWarning(
                "Worker: User {UserId} gui media khong ton tai vao Room {RoomId}. Expected={Expected}; Actual={Actual}",
                command.SenderId,
                command.RoomId,
                command.MediaIds.Count,
                mediaAssets.Count);
            return null;
        }

        var assetsById = mediaAssets.ToDictionary(asset => asset.Id);
        var orderedAssets = new List<MediaAsset>(command.MediaIds.Count);

        foreach (var mediaId in command.MediaIds)
        {
            if (!assetsById.TryGetValue(mediaId, out var mediaAsset) ||
                mediaAsset.Scope != MediaScope.ChatAttachment ||
                mediaAsset.OwnerUserId != command.SenderId ||
                mediaAsset.Status != MediaAssetStatus.Pending ||
                mediaAsset.RoomId != null ||
                mediaAsset.DeletedAt != null)
            {
                _logger.LogWarning(
                    "Worker: User {UserId} gui media {MediaId} khong hop le vao Room {RoomId}.",
                    command.SenderId,
                    mediaId,
                    command.RoomId);
                return null;
            }

            orderedAssets.Add(mediaAsset);
        }

        return orderedAssets;
    }

    private static List<Attachment> BuildAttachmentSnapshots(
        IReadOnlyList<MediaAsset> mediaAssets,
        IMediaStorageService mediaStorageService,
        MediaStorageOptions mediaOptions,
        DateTime now,
        bool includeAccessUrls)
    {
        if (mediaAssets.Count == 0)
            return [];

        var ttl = TimeSpan.FromMinutes(Math.Max(1, mediaOptions.SignedUrlMinutes));
        var expiresAt = now.Add(ttl);

        return mediaAssets
            .Select(asset =>
            {
                var isPublic = asset.AccessLevel == MediaAccessLevel.PublicRead &&
                               !string.IsNullOrWhiteSpace(asset.PublicUrl);
                var url = string.Empty;
                DateTime? urlExpiresAt = null;

                if (includeAccessUrls)
                {
                    url = isPublic
                        ? asset.PublicUrl!
                        : mediaStorageService.CreatePresignedGetUrl(asset.BucketName, asset.StorageKey, ttl);
                    urlExpiresAt = isPublic ? null : expiresAt;
                }

                return new Attachment
                {
                    MediaId = asset.Id,
                    Kind = asset.Kind,
                    Filename = asset.OriginalFileName,
                    Size = asset.SizeBytes,
                    MimeType = asset.ContentType,
                    Url = url,
                    ExpiresAt = urlExpiresAt
                };
            })
            .ToList();
    }

    private static Message CloneMessageForDelivery(Message source, List<Attachment> deliveryAttachments)
    {
        return new Message
        {
            Id = source.Id,
            RoomId = source.RoomId,
            SenderId = source.SenderId,
            Type = source.Type,
            Content = source.Content,
            Attachments = deliveryAttachments.Count == 0 ? null : deliveryAttachments,
            Meta = source.Meta,
            Status = source.Status,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            DeletedAt = source.DeletedAt
        };
    }

    private static MessageType DetermineMessageType(string content, IReadOnlyList<Attachment> attachments)
    {
        if (!string.IsNullOrWhiteSpace(content) || attachments.Count == 0)
            return MessageType.Text;

        if (attachments.Count > 1)
            return MessageType.File;

        return attachments[0].Kind switch
        {
            MediaKind.Image => MessageType.Image,
            MediaKind.Audio => MessageType.Audio,
            MediaKind.Video => MessageType.Video,
            _ => MessageType.File
        };
    }

    private async Task NotifyMessageFailedAsync(SendMessageCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TempId))
            return;

        await _hubContext.Clients.User(command.SenderId.ToString())
            .MessageStatusUpdated(command.TempId, command.TempId, "Failed");
    }

    private static async Task AcknowledgeAndDeleteAsync(
        IDatabase redisDb,
        RedisKey streamName,
        RedisValue groupName,
        RedisValue messageId)
    {
        await redisDb.StreamAcknowledgeAsync(streamName, groupName, messageId);
        await redisDb.StreamDeleteAsync(streamName, [messageId]);
    }
}
