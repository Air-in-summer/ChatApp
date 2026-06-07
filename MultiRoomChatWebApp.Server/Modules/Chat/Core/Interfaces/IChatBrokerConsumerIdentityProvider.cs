using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Sinh consumer name duy nhat cho worker doc chat broker V2.
/// </summary>
public interface IChatBrokerConsumerIdentityProvider
{
    string InstanceId { get; }

    RedisValue GetConsumerName(
        ChatBrokerConsumerKind consumerKind,
        int workerId);
}
