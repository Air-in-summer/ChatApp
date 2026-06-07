using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Ket noi rieng den broker xu ly su kien chat, tach khoi Redis cache/presence.
/// </summary>
public interface IChatBrokerConnection
{
    /// <summary>
    /// Tra ve database Redis danh rieng cho broker chat.
    /// </summary>
    IDatabase GetDatabase();
}
