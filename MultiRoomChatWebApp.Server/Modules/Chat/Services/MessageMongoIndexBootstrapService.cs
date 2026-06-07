using MongoDB.Bson;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Khởi tạo các index bắt buộc cho định danh tin nhắn.
/// </summary>
public sealed class MessageMongoIndexBootstrapService : IHostedService
{
    private readonly IMongoClient _mongoClient;
    private readonly ILogger<MessageMongoIndexBootstrapService> _logger;

    public MessageMongoIndexBootstrapService(
        IMongoClient mongoClient,
        ILogger<MessageMongoIndexBootstrapService> logger)
    {
        _mongoClient = mongoClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var collection = _mongoClient
            .GetDatabase("ChatAppDB_Mongo")
            .GetCollection<Message>("messages");
        var clientMessageIdExists = new BsonDocumentFilterDefinition<Message>(
            new BsonDocument("client_message_id", new BsonDocument("$type", "string")));
        var pinnedAtExists = new BsonDocumentFilterDefinition<Message>(
            new BsonDocument("pinned_at", new BsonDocument("$type", "date")));

        var requiredIndexes = new[]
        {
            new RequiredMessageIndex(
                new BsonDocument
                {
                    { "sender_id", 1 },
                    { "client_message_id", 1 }
                },
                new CreateIndexModel<Message>(
                    Builders<Message>.IndexKeys
                        .Ascending(message => message.SenderId)
                        .Ascending(message => message.ClientMessageId),
                    new CreateIndexOptions<Message>
                    {
                        Name = "ux_messages_sender_client_message",
                        Unique = true,
                        PartialFilterExpression = clientMessageIdExists
                    }),
                RequireUnique: true),
            new RequiredMessageIndex(
                new BsonDocument
                {
                    { "room_id", 1 },
                    { "_id", -1 }
                },
                new CreateIndexModel<Message>(
                    Builders<Message>.IndexKeys
                        .Ascending(message => message.RoomId)
                        .Descending(message => message.Id),
                    new CreateIndexOptions<Message>
                    {
                        Name = "ix_messages_room_id_desc"
                    })),
            new RequiredMessageIndex(
                new BsonDocument
                {
                    { "room_id", 1 },
                    { "pinned_at", -1 }
                },
                new CreateIndexModel<Message>(
                    Builders<Message>.IndexKeys
                        .Ascending(message => message.RoomId)
                        .Descending(message => message.PinnedAt),
                    new CreateIndexOptions<Message>
                    {
                        Name = "ix_messages_room_pinned_at_desc",
                        PartialFilterExpression = pinnedAtExists
                    }))
        };
        var existingIndexes = await ListExistingIndexesAsync(
            collection,
            cancellationToken);
        var indexesToCreate = requiredIndexes
            .Where(requiredIndex => !HasCompatibleIndex(
                existingIndexes,
                requiredIndex))
            .Select(requiredIndex => requiredIndex.Model)
            .ToList();

        if (indexesToCreate.Count > 0)
        {
            await collection.Indexes.CreateManyAsync(
                indexesToCreate,
                cancellationToken: cancellationToken);
        }

        _logger.LogInformation(
            "Đã bảo đảm MongoDB indexes cho định danh tin nhắn. Created={CreatedCount}; Existing={ExistingCount}",
            indexesToCreate.Count,
            requiredIndexes.Length - indexesToCreate.Count);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<IReadOnlyList<BsonDocument>> ListExistingIndexesAsync(
        IMongoCollection<Message> collection,
        CancellationToken cancellationToken)
    {
        using var cursor = await collection.Indexes.ListAsync(cancellationToken);
        return await cursor.ToListAsync(cancellationToken);
    }

    private static bool HasCompatibleIndex(
        IReadOnlyList<BsonDocument> existingIndexes,
        RequiredMessageIndex requiredIndex)
    {
        var existingIndex = existingIndexes.FirstOrDefault(index =>
            index.TryGetValue("key", out var key) &&
            key.IsBsonDocument &&
            HasSameKeyPattern(key.AsBsonDocument, requiredIndex.Key));
        if (existingIndex is null)
        {
            return false;
        }

        if (!requiredIndex.RequireUnique)
        {
            return true;
        }

        if (existingIndex.TryGetValue("unique", out var unique) &&
            unique.IsBoolean &&
            unique.AsBoolean)
        {
            return true;
        }

        throw new InvalidOperationException(
            $"MongoDB index with key {requiredIndex.Key} already exists but is not unique.");
    }

    private static bool HasSameKeyPattern(
        BsonDocument existingKey,
        BsonDocument requiredKey)
    {
        if (existingKey.ElementCount != requiredKey.ElementCount)
        {
            return false;
        }

        for (var index = 0; index < requiredKey.ElementCount; index++)
        {
            var existingElement = existingKey.GetElement(index);
            var requiredElement = requiredKey.GetElement(index);
            if (existingElement.Name != requiredElement.Name ||
                existingElement.Value != requiredElement.Value)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record RequiredMessageIndex(
        BsonDocument Key,
        CreateIndexModel<Message> Model,
        bool RequireUnique = false);
}
