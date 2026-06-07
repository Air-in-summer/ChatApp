using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Quan ly vong doi ket noi Redis dung rieng cho broker chat.
/// </summary>
public sealed class RedisChatBrokerConnection : IChatBrokerConnection, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly int _database;

    public RedisChatBrokerConnection(
        IConnectionMultiplexer connectionMultiplexer,
        int database)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _database = database;
    }

    /// <inheritdoc />
    public IDatabase GetDatabase()
    {
        return _connectionMultiplexer.GetDatabase(_database);
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionMultiplexer.CloseAsync();
        _connectionMultiplexer.Dispose();
    }
}
