using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Khoi tao stream va consumer group cua chat broker V2 khi ung dung start.
/// </summary>
public sealed class ChatBrokerBootstrapService : IHostedService
{
    private static readonly RedisValue StartFromBeginning = "0-0";

    private readonly IChatBrokerConnection _brokerConnection;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly ILogger<ChatBrokerBootstrapService> _logger;

    public ChatBrokerBootstrapService(
        IChatBrokerConnection brokerConnection,
        IChatBrokerKeyProvider keyProvider,
        ILogger<ChatBrokerBootstrapService> logger)
    {
        _brokerConnection = brokerConnection;
        _keyProvider = keyProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var database = _brokerConnection.GetDatabase();

        await EnsureConsumerGroupAsync(
            database,
            _keyProvider.MessageStream,
            _keyProvider.PersistenceGroup);
        await EnsureConsumerGroupAsync(
            database,
            _keyProvider.MessageStream,
            _keyProvider.DeliveryGroup);

        _logger.LogInformation(
            "Chat broker bootstrap thanh cong. Stream={Stream}; PersistenceGroup={PersistenceGroup}; DeliveryGroup={DeliveryGroup}",
            _keyProvider.MessageStream,
            _keyProvider.PersistenceGroup,
            _keyProvider.DeliveryGroup);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task EnsureConsumerGroupAsync(
        IDatabase database,
        RedisKey streamName,
        RedisValue groupName)
    {
        try
        {
            await database.StreamCreateConsumerGroupAsync(
                streamName,
                groupName,
                StartFromBeginning,
                createStream: true);

            _logger.LogInformation(
                "Da tao chat broker consumer group. Stream={Stream}; Group={Group}",
                streamName,
                groupName);
        }
        catch (RedisServerException ex) when (IsBusyGroup(ex))
        {
            _logger.LogInformation(
                "Chat broker consumer group da ton tai. Stream={Stream}; Group={Group}",
                streamName,
                groupName);
        }
    }

    private static bool IsBusyGroup(RedisServerException exception)
    {
        return exception.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase);
    }
}
