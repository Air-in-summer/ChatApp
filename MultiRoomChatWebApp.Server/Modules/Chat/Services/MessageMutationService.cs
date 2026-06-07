using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Shared.Exceptions;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Thuc thi cac thay doi tren tin nhan va phat snapshot moi nhat toi dung nguoi nhan.
/// </summary>
public sealed class MessageMutationService : IMessageMutationService
{
    private const int MaxEmojiTextElements = 16;
    private const int MaxPinnedMessagesPerRoom = 50;
    private static readonly TimeSpan PinLockTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PinLockWaitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PinLockRetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly IMongoCollection<Message> _messages;
    private readonly IDatabase _redisDatabase;
    private readonly IMessageMutationPermissionService _permissionService;
    private readonly IMessageDeliveryRecipientResolver _recipientResolver;
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<MessageMutationService> _logger;

    public MessageMutationService(
        IMongoDatabase mongoDatabase,
        IConnectionMultiplexer redis,
        IMessageMutationPermissionService permissionService,
        IMessageDeliveryRecipientResolver recipientResolver,
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<MessageMutationService> logger)
    {
        _messages = mongoDatabase.GetCollection<Message>("messages");
        _redisDatabase = redis.GetDatabase();
        _permissionService = permissionService;
        _recipientResolver = recipientResolver;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MessageEditedDto> EditMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        EditMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (message, _) = await LoadAuthorizedMessageAsync(
            actorId,
            roomId,
            messageId,
            cancellationToken);

        if (!_permissionService.IsAuthor(message, actorId))
        {
            throw ApiException.Forbidden(
                "message_edit_forbidden",
                "Ban chi co the sua tin nhan cua minh.");
        }

        EnsureMessageIsActive(message);

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length > MessageAcceptedEventV1.MaxContentLength)
        {
            throw ApiException.BadRequest(
                "message_content_too_long",
                "Noi dung tin nhan vuot qua gioi han cho phep.");
        }

        if (string.IsNullOrWhiteSpace(content) &&
            (message.Attachments is null || message.Attachments.Count == 0))
        {
            throw ApiException.BadRequest(
                "message_empty",
                "Noi dung tin nhan khong duoc de trong.");
        }

        var updatedAt = DateTime.UtcNow;
        var filter = BuildActiveMessageFilter(roomId, messageId) &
                     Builders<Message>.Filter.Eq(item => item.SenderId, actorId);
        var update = Builders<Message>.Update
            .Set(item => item.Content, content)
            .Set(item => item.EditedAt, updatedAt)
            .Set(item => item.UpdatedAt, updatedAt);

        var updatedMessage = await _messages.FindOneAndUpdateAsync(
            filter,
            update,
            ReturnUpdatedMessageOptions,
            cancellationToken);

        if (updatedMessage is null)
        {
            var currentMessage = await GetMessageAsync(roomId, messageId, cancellationToken);
            EnsureMessageIsActive(currentMessage);
            throw ApiException.Conflict(
                "message_edit_conflict",
                "Tin nhan da thay doi, vui long tai lai va thu lai.");
        }

        var payload = new MessageEditedDto(
            updatedMessage.RoomId,
            updatedMessage.Id,
            updatedMessage.Content,
            updatedMessage.EditedAt!.Value,
            updatedMessage.UpdatedAt!.Value);

        await BroadcastAsync(
            roomId,
            actorId,
            client => client.MessageEdited(payload));

        return payload;
    }

    /// <inheritdoc />
    public async Task<MessageDeletedDto> DeleteMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var (message, roomContext) = await LoadAuthorizedMessageAsync(
            actorId,
            roomId,
            messageId,
            cancellationToken);

        var canDelete = _permissionService.IsAuthor(message, actorId) ||
                        await _permissionService.IsGroupRoomModeratorAsync(
                            roomContext,
                            actorId,
                            cancellationToken);

        if (!canDelete)
        {
            throw ApiException.Forbidden(
                "message_delete_forbidden",
                "Ban khong co quyen xoa tin nhan nay.");
        }

        if (message.DeletedAt.HasValue)
        {
            return ToDeletedDto(message);
        }

        var deletedAt = DateTime.UtcNow;
        var update = Builders<Message>.Update
            .Set(item => item.Content, string.Empty)
            .Set(item => item.Attachments, (List<Attachment>?)null)
            .Set(item => item.Reactions, [])
            .Set(item => item.PinnedAt, (DateTime?)null)
            .Set(item => item.PinnedBy, (Guid?)null)
            .Set(item => item.DeletedAt, deletedAt)
            .Set(item => item.DeletedBy, actorId)
            .Set(item => item.UpdatedAt, deletedAt);

        var deletedMessage = await _messages.FindOneAndUpdateAsync(
            BuildActiveMessageFilter(roomId, messageId),
            update,
            ReturnUpdatedMessageOptions,
            cancellationToken);

        if (deletedMessage is null)
        {
            var currentMessage = await GetMessageAsync(roomId, messageId, cancellationToken);
            if (currentMessage.DeletedAt.HasValue)
            {
                return ToDeletedDto(currentMessage);
            }

            throw ApiException.Conflict(
                "message_delete_conflict",
                "Tin nhan da thay doi, vui long tai lai va thu lai.");
        }

        var payload = ToDeletedDto(deletedMessage);
        await BroadcastAsync(
            roomId,
            actorId,
            client => client.MessageDeleted(payload));

        return payload;
    }

    /// <inheritdoc />
    public async Task<MessageReactionUpdatedDto> AddReactionAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmoji = NormalizeEmoji(emoji);
        var (message, _) = await LoadAuthorizedMessageAsync(
            actorId,
            roomId,
            messageId,
            cancellationToken);
        EnsureMessageIsActive(message);

        var reactionExists = Builders<Message>.Filter.ElemMatch(
            item => item.Reactions,
            reaction => reaction.UserId == actorId &&
                        reaction.Emoji == normalizedEmoji);
        var filter = BuildActiveMessageFilter(roomId, messageId) &
                     Builders<Message>.Filter.Not(reactionExists);
        var reaction = new MessageReaction
        {
            Emoji = normalizedEmoji,
            UserId = actorId,
            CreatedAt = DateTime.UtcNow
        };

        var updatedMessage = await _messages.FindOneAndUpdateAsync(
            filter,
            Builders<Message>.Update.Push(item => item.Reactions, reaction),
            ReturnUpdatedMessageOptions,
            cancellationToken);

        if (updatedMessage is null)
        {
            updatedMessage = await GetMessageAsync(roomId, messageId, cancellationToken);
            EnsureMessageIsActive(updatedMessage);
        }
        else
        {
            var changedPayload = ToReactionUpdatedDto(updatedMessage);
            await BroadcastAsync(
                roomId,
                actorId,
                client => client.MessageReactionUpdated(changedPayload));
            return changedPayload;
        }

        return ToReactionUpdatedDto(updatedMessage);
    }

    /// <inheritdoc />
    public async Task<MessageReactionUpdatedDto> RemoveReactionAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmoji = NormalizeEmoji(emoji);
        var (message, _) = await LoadAuthorizedMessageAsync(
            actorId,
            roomId,
            messageId,
            cancellationToken);
        EnsureMessageIsActive(message);

        var reactionExists = Builders<Message>.Filter.ElemMatch(
            item => item.Reactions,
            reaction => reaction.UserId == actorId &&
                        reaction.Emoji == normalizedEmoji);
        var filter = BuildActiveMessageFilter(roomId, messageId) & reactionExists;
        var update = Builders<Message>.Update.PullFilter(
            item => item.Reactions,
            reaction => reaction.UserId == actorId &&
                        reaction.Emoji == normalizedEmoji);

        var updatedMessage = await _messages.FindOneAndUpdateAsync(
            filter,
            update,
            ReturnUpdatedMessageOptions,
            cancellationToken);

        if (updatedMessage is null)
        {
            updatedMessage = await GetMessageAsync(roomId, messageId, cancellationToken);
            EnsureMessageIsActive(updatedMessage);
        }
        else
        {
            var changedPayload = ToReactionUpdatedDto(updatedMessage);
            await BroadcastAsync(
                roomId,
                actorId,
                client => client.MessageReactionUpdated(changedPayload));
            return changedPayload;
        }

        return ToReactionUpdatedDto(updatedMessage);
    }

    /// <inheritdoc />
    public async Task<MessagePinnedDto> PinMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var (message, roomContext) = await LoadAuthorizedMessageAsync(
            actorId,
            roomId,
            messageId,
            cancellationToken);
        EnsureMessageIsActive(message);
        await EnsureCanManagePinAsync(roomContext, actorId, cancellationToken);

        if (message.PinnedAt.HasValue && message.PinnedBy.HasValue)
        {
            return ToPinnedDto(message);
        }

        var lockValue = await AcquirePinLockAsync(roomId, cancellationToken);
        MessagePinnedDto payload;
        try
        {
            var currentMessage = await GetMessageAsync(
                roomId,
                messageId,
                cancellationToken);
            EnsureMessageIsActive(currentMessage);
            if (currentMessage.PinnedAt.HasValue &&
                currentMessage.PinnedBy.HasValue)
            {
                return ToPinnedDto(currentMessage);
            }

            var pinnedCount = await _messages.CountDocumentsAsync(
                BuildRoomPinnedFilter(roomId),
                cancellationToken: cancellationToken);
            if (pinnedCount >= MaxPinnedMessagesPerRoom)
            {
                throw ApiException.BadRequest(
                    "message_pin_limit_reached",
                    "Phong da dat gioi han 50 tin nhan duoc ghim.");
            }

            var pinnedAt = DateTime.UtcNow;
            var filter = BuildActiveMessageFilter(roomId, messageId) &
                         Builders<Message>.Filter.Eq(item => item.PinnedAt, null);
            var update = Builders<Message>.Update
                .Set(item => item.PinnedAt, pinnedAt)
                .Set(item => item.PinnedBy, actorId)
                .Set(item => item.UpdatedAt, pinnedAt);

            var pinnedMessage = await _messages.FindOneAndUpdateAsync(
                filter,
                update,
                ReturnUpdatedMessageOptions,
                cancellationToken);

            if (pinnedMessage is null)
            {
                currentMessage = await GetMessageAsync(
                    roomId,
                    messageId,
                    cancellationToken);
                EnsureMessageIsActive(currentMessage);
                if (currentMessage.PinnedAt.HasValue &&
                    currentMessage.PinnedBy.HasValue)
                {
                    return ToPinnedDto(currentMessage);
                }

                throw ApiException.Conflict(
                    "message_pin_conflict",
                    "Trang thai ghim da thay doi, vui long tai lai va thu lai.");
            }

            payload = ToPinnedDto(pinnedMessage);
        }
        finally
        {
            await ReleasePinLockAsync(roomId, lockValue);
        }

        await BroadcastAsync(
            roomId,
            actorId,
            client => client.MessagePinned(payload));

        return payload;
    }

    /// <inheritdoc />
    public async Task<MessageUnpinnedDto> UnpinMessageAsync(
        Guid actorId,
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var (message, roomContext) = await LoadAuthorizedMessageAsync(
            actorId,
            roomId,
            messageId,
            cancellationToken);
        EnsureMessageIsActive(message);
        await EnsureCanManagePinAsync(roomContext, actorId, cancellationToken);

        var payload = new MessageUnpinnedDto(roomId, messageId);
        if (!message.PinnedAt.HasValue)
        {
            return payload;
        }

        var lockValue = await AcquirePinLockAsync(roomId, cancellationToken);
        var stateChanged = false;
        try
        {
            var currentMessage = await GetMessageAsync(roomId, messageId, cancellationToken);
            EnsureMessageIsActive(currentMessage);
            if (!currentMessage.PinnedAt.HasValue)
            {
                return payload;
            }

            var updatedAt = DateTime.UtcNow;
            var filter = BuildActiveMessageFilter(roomId, messageId) &
                         Builders<Message>.Filter.Ne(item => item.PinnedAt, null);
            var update = Builders<Message>.Update
                .Set(item => item.PinnedAt, (DateTime?)null)
                .Set(item => item.PinnedBy, (Guid?)null)
                .Set(item => item.UpdatedAt, updatedAt);

            var unpinnedMessage = await _messages.FindOneAndUpdateAsync(
                filter,
                update,
                ReturnUpdatedMessageOptions,
                cancellationToken);

            if (unpinnedMessage is null)
            {
                currentMessage = await GetMessageAsync(
                    roomId,
                    messageId,
                    cancellationToken);
                EnsureMessageIsActive(currentMessage);
                if (!currentMessage.PinnedAt.HasValue)
                {
                    return payload;
                }

                throw ApiException.Conflict(
                    "message_unpin_conflict",
                    "Trang thai ghim da thay doi, vui long tai lai va thu lai.");
            }

            stateChanged = true;
        }
        finally
        {
            await ReleasePinLockAsync(roomId, lockValue);
        }

        if (stateChanged)
        {
            await BroadcastAsync(
                roomId,
                actorId,
                client => client.MessageUnpinned(payload));
        }

        return payload;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetPinnedMessagesAsync(
        Guid actorId,
        Guid roomId,
        int limit = MaxPinnedMessagesPerRoom,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || limit > MaxPinnedMessagesPerRoom)
        {
            throw ApiException.BadRequest(
                "message_pin_limit_invalid",
                "Gioi han tin nhan duoc ghim phai tu 1 den 50.");
        }

        await _permissionService.EnsureCanReadRoomAsync(
            roomId,
            actorId,
            cancellationToken);

        return await _messages
            .Find(BuildRoomPinnedFilter(roomId))
            .SortByDescending(message => message.PinnedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<(Message Message, MessageMutationRoomContext RoomContext)>
        LoadAuthorizedMessageAsync(
            Guid actorId,
            Guid roomId,
            string messageId,
            CancellationToken cancellationToken)
    {
        var roomContext = await _permissionService.EnsureCanReadRoomAsync(
            roomId,
            actorId,
            cancellationToken);
        var message = await GetMessageAsync(roomId, messageId, cancellationToken);
        return (message, roomContext);
    }

    private async Task<Message> GetMessageAsync(
        Guid roomId,
        string messageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId) ||
            !ObjectId.TryParse(messageId, out _))
        {
            throw ApiException.BadRequest(
                "message_id_invalid",
                "Ma tin nhan khong hop le.");
        }

        var message = await _messages
            .Find(BuildMessageFilter(roomId, messageId))
            .FirstOrDefaultAsync(cancellationToken);

        return message ?? throw ApiException.NotFound(
            "message_not_found",
            "Khong tim thay tin nhan trong phong nay.");
    }

    private async Task EnsureCanManagePinAsync(
        MessageMutationRoomContext roomContext,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (roomContext.RoomType == RoomType.DirectMessage)
        {
            return;
        }

        if (await _permissionService.IsGroupRoomModeratorAsync(
                roomContext,
                actorId,
                cancellationToken))
        {
            return;
        }

        throw ApiException.Forbidden(
            "message_pin_forbidden",
            "Ban khong co quyen thay doi tin nhan duoc ghim trong phong nay.");
    }

    private async Task<RedisValue> AcquirePinLockAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        var lockKey = BuildPinLockKey(roomId);
        var lockValue = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var deadline = DateTime.UtcNow + PinLockWaitTimeout;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _redisDatabase.LockTakeAsync(lockKey, lockValue, PinLockTtl))
            {
                return lockValue;
            }

            await Task.Delay(PinLockRetryDelay, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        throw ApiException.Conflict(
            "message_pin_busy",
            "Danh sach tin nhan duoc ghim dang thay doi, vui long thu lai.");
    }

    private static RedisKey BuildPinLockKey(Guid roomId)
        => $"chat:room:{roomId:N}:pins:lock";

    private async Task ReleasePinLockAsync(
        Guid roomId,
        RedisValue lockValue)
    {
        try
        {
            await _redisDatabase.LockReleaseAsync(
                BuildPinLockKey(roomId),
                lockValue);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Khong the giai phong pin lock; lock se tu het han. RoomId={RoomId}",
                roomId);
        }
    }

    private async Task BroadcastAsync(
        Guid roomId,
        Guid actorId,
        Func<IChatClient, Task> publish)
    {
        var recipientResult = await _recipientResolver.ResolveAsync(
            roomId,
            actorId,
            CancellationToken.None);

        if (!recipientResult.IsSuccess)
        {
            _logger.LogWarning(
                "Khong the phat message mutation. RoomId={RoomId}; ActorId={ActorId}; Code={Code}; Reason={Reason}",
                roomId,
                actorId,
                recipientResult.ErrorCode,
                recipientResult.ErrorReason);
            return;
        }

        var recipientIds = recipientResult.RecipientIds
            .Select(userId => userId.ToString())
            .ToList();
        if (recipientIds.Count == 0)
        {
            return;
        }

        await publish(_hubContext.Clients.Users(recipientIds));
    }

    private static FilterDefinition<Message> BuildMessageFilter(
        Guid roomId,
        string messageId)
    {
        return Builders<Message>.Filter.Eq(message => message.RoomId, roomId) &
               Builders<Message>.Filter.Eq(message => message.Id, messageId);
    }

    private static FilterDefinition<Message> BuildActiveMessageFilter(
        Guid roomId,
        string messageId)
    {
        return BuildMessageFilter(roomId, messageId) &
               Builders<Message>.Filter.Eq(message => message.DeletedAt, null);
    }

    private static FilterDefinition<Message> BuildRoomPinnedFilter(Guid roomId)
    {
        return Builders<Message>.Filter.Eq(message => message.RoomId, roomId) &
               Builders<Message>.Filter.Eq(message => message.DeletedAt, null) &
               Builders<Message>.Filter.Ne(message => message.PinnedAt, null);
    }

    private static void EnsureMessageIsActive(Message message)
    {
        if (message.DeletedAt.HasValue)
        {
            throw ApiException.Conflict(
                "message_deleted",
                "Tin nhan da bi xoa.");
        }
    }

    private static string NormalizeEmoji(string emoji)
    {
        var normalized = emoji?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw ApiException.BadRequest(
                "message_reaction_empty",
                "Reaction khong duoc de trong.");
        }

        if (new StringInfo(normalized).LengthInTextElements > MaxEmojiTextElements)
        {
            throw ApiException.BadRequest(
                "message_reaction_too_long",
                "Reaction khong duoc vuot qua 16 ky tu.");
        }

        return normalized;
    }

    private static MessageDeletedDto ToDeletedDto(Message message)
    {
        return new MessageDeletedDto(
            message.RoomId,
            message.Id,
            message.DeletedAt!.Value,
            message.DeletedBy ?? message.SenderId);
    }

    private static MessagePinnedDto ToPinnedDto(Message message)
    {
        return new MessagePinnedDto(
            message.RoomId,
            message.Id,
            message.PinnedAt!.Value,
            message.PinnedBy!.Value);
    }

    private static MessageReactionUpdatedDto ToReactionUpdatedDto(Message message)
    {
        var reactions = (message.Reactions ?? [])
            .OrderBy(reaction => reaction.CreatedAt)
            .Select(reaction => new MessageReactionDto(
                reaction.Emoji,
                reaction.UserId,
                reaction.CreatedAt))
            .ToList();

        return new MessageReactionUpdatedDto(
            message.RoomId,
            message.Id,
            reactions);
    }

    private static FindOneAndUpdateOptions<Message> ReturnUpdatedMessageOptions { get; } =
        new()
        {
            ReturnDocument = ReturnDocument.After
        };
}
