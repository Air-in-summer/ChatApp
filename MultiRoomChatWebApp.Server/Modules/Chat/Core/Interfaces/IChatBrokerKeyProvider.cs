using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Cung cap tap trung ten stream, consumer group va key Redis cua chat broker V2.
/// </summary>
public interface IChatBrokerKeyProvider
{
    RedisKey MessageStream { get; }

    RedisValue PersistenceGroup { get; }

    RedisValue DeliveryGroup { get; }

    RedisKey PersistenceDeadLetterStream { get; }

    RedisKey DeliveryDeadLetterStream { get; }

    RedisKey MaintenanceLockKey { get; }

    RedisKey MaintenanceStateKey { get; }

    RedisKey BuildSendMarkerKey(Guid senderId, Guid clientMessageId);

    RedisKey BuildRetryStateKey(
        ChatBrokerConsumerKind consumerKind,
        RedisValue entryId);

}
