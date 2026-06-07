using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;
using Microsoft.Extensions.Options;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Cấp định danh ổn định cho một lần gửi bằng marker idempotency chung trên Redis broker.
/// </summary>
public sealed class RedisMessageIdentityService : IMessageIdentityService
{
    private const string ResolveScript = """
local existing_message_id = redis.call('HGET', KEYS[1], 'messageId')
if existing_message_id then
    return {
        existing_message_id,
        redis.call('HGET', KEYS[1], 'acceptedAtUtc')
    }
end

redis.call(
    'HSET',
    KEYS[1],
    'messageId', ARGV[1],
    'acceptedAtUtc', ARGV[2])
redis.call('PEXPIRE', KEYS[1], ARGV[3])

return {
    ARGV[1],
    ARGV[2]
}
""";

    private readonly IChatBrokerConnection _brokerConnection;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly IMongoCollection<Message> _messagesCollection;
    private readonly ChatBrokerOptions _options;

    public RedisMessageIdentityService(
        IChatBrokerConnection brokerConnection,
        IChatBrokerKeyProvider keyProvider,
        IMongoClient mongoClient,
        IOptions<ChatBrokerOptions> options)
    {
        _brokerConnection = brokerConnection;
        _keyProvider = keyProvider;
        _messagesCollection = mongoClient
            .GetDatabase("ChatAppDB_Mongo")
            .GetCollection<Message>("messages");
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<MessageIdentity> ResolveAsync(Guid senderId, Guid clientMessageId)
    {
        if (senderId == Guid.Empty)
            throw new ArgumentException("SenderId không hợp lệ.", nameof(senderId));

        if (clientMessageId == Guid.Empty)
            throw new ArgumentException("ClientMessageId không hợp lệ.", nameof(clientMessageId));

        var persistedMessage = await _messagesCollection
            .Find(message =>
                message.SenderId == senderId &&
                message.ClientMessageId == clientMessageId)
            .SortByDescending(message => message.Id)
            .FirstOrDefaultAsync();
        if (persistedMessage is not null)
        {
            return new MessageIdentity(
                persistedMessage.Id,
                persistedMessage.AcceptedAt ?? persistedMessage.CreatedAt);
        }

        var candidateMessageId = ObjectId.GenerateNewId().ToString();
        var acceptedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var ttlMs = ((long)Math.Ceiling(_options.IdempotencyTtl.TotalMilliseconds))
            .ToString(CultureInfo.InvariantCulture);
        var rawResult = await _brokerConnection.GetDatabase().ScriptEvaluateAsync(
            ResolveScript,
            [_keyProvider.BuildSendMarkerKey(senderId, clientMessageId)],
            [candidateMessageId, acceptedAtUtc, ttlMs]);

        var values = (RedisResult[]?)rawResult
            ?? throw new InvalidOperationException("Ket qua cap dinh danh message rong.");
        if (values.Length != 2)
        {
            throw new InvalidOperationException(
                $"Ket qua cap dinh danh message khong hop le. Length={values.Length}.");
        }

        return new MessageIdentity(
            values[0].ToString(),
            DateTime.Parse(
                values[1].ToString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }
}
