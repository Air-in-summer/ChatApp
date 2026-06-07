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

        var indexes = new[]
        {
            new CreateIndexModel<Message>(
                Builders<Message>.IndexKeys
                    .Ascending(message => message.SenderId)
                    .Ascending(message => message.ClientMessageId),
                new CreateIndexOptions<Message>
                {
                    Name = "ux_messages_sender_client_message",
                    Unique = true,
                    PartialFilterExpression = clientMessageIdExists
                })
        };

        await collection.Indexes.CreateManyAsync(indexes, cancellationToken);
        _logger.LogInformation("Đã bảo đảm MongoDB indexes cho định danh tin nhắn.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
