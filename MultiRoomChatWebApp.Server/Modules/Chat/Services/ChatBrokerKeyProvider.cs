using Microsoft.Extensions.Options;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Gom cac ten/key cua broker V2 vao mot cho de tranh hard-code rai rac.
/// </summary>
public sealed class ChatBrokerKeyProvider : IChatBrokerKeyProvider
{
    private readonly string _hashTag;

    public ChatBrokerKeyProvider(IOptions<ChatBrokerOptions> options)
    {
        var brokerOptions = options.Value;
        MessageStream = brokerOptions.StreamName;
        PersistenceGroup = brokerOptions.PersistenceGroupName;
        DeliveryGroup = brokerOptions.DeliveryGroupName;

        _hashTag = ExtractHashTag(brokerOptions.StreamName)
            ?? throw new InvalidOperationException(
                "ChatBroker:StreamName phai co Redis hash tag, vi du chat:{messages}:v1.");

        PersistenceDeadLetterStream = BuildKey("dlq:persistence:v1");
        DeliveryDeadLetterStream = BuildKey("dlq:delivery:v1");
        MaintenanceLockKey = BuildKey("maintenance:lock:v1");
        MaintenanceStateKey = BuildKey("maintenance:state:v1");
    }

    public RedisKey MessageStream { get; }

    public RedisValue PersistenceGroup { get; }

    public RedisValue DeliveryGroup { get; }

    public RedisKey PersistenceDeadLetterStream { get; }

    public RedisKey DeliveryDeadLetterStream { get; }

    public RedisKey MaintenanceLockKey { get; }

    public RedisKey MaintenanceStateKey { get; }

    public RedisKey BuildSendMarkerKey(Guid senderId, Guid clientMessageId)
    {
        return BuildKey($"send:v1:{senderId:N}:{clientMessageId:N}");
    }

    public RedisKey BuildRetryStateKey(
        ChatBrokerConsumerKind consumerKind,
        RedisValue entryId)
    {
        if (entryId.IsNullOrEmpty)
        {
            throw new ArgumentException("EntryId khong duoc rong.", nameof(entryId));
        }

        var consumerSegment = consumerKind switch
        {
            ChatBrokerConsumerKind.Persistence => "persistence",
            ChatBrokerConsumerKind.Delivery => "delivery",
            _ => throw new ArgumentOutOfRangeException(
                nameof(consumerKind),
                consumerKind,
                "Loai consumer khong hop le.")
        };

        return BuildKey($"retry:{consumerSegment}:v1:{entryId}");
    }

    private RedisKey BuildKey(string suffix)
    {
        return $"chat:{{{_hashTag}}}:{suffix}";
    }

    private static string? ExtractHashTag(string key)
    {
        var start = key.IndexOf('{', StringComparison.Ordinal);
        var end = key.IndexOf('}', StringComparison.Ordinal);

        if (start < 0 || end <= start + 1)
            return null;

        return key[(start + 1)..end];
    }
}
