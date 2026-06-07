using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Tao consumer name theo instance de nhieu worker/app instance khong dung chung mot consumer.
/// </summary>
public sealed class ChatBrokerConsumerIdentityProvider : IChatBrokerConsumerIdentityProvider
{
    private readonly string _consumerNamePrefix;

    public ChatBrokerConsumerIdentityProvider(IOptions<ChatBrokerOptions> options)
    {
        var brokerOptions = options.Value;
        _consumerNamePrefix = NormalizeSegment(brokerOptions.ConsumerNamePrefix);
        InstanceId = ResolveInstanceId(brokerOptions.InstanceIdEnvironmentVariable);
    }

    public string InstanceId { get; }

    public RedisValue GetConsumerName(
        ChatBrokerConsumerKind consumerKind,
        int workerId)
    {
        if (workerId < 0)
            throw new ArgumentOutOfRangeException(nameof(workerId), "WorkerId khong duoc am.");

        return $"{_consumerNamePrefix}:{InstanceId}:{ToConsumerKindSegment(consumerKind)}:{workerId}";
    }

    private static string ResolveInstanceId(string environmentVariableName)
    {
        var configuredInstanceId = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(configuredInstanceId))
            return NormalizeSegment(configuredInstanceId);

        var localInstanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        if (localInstanceId.Length > 48)
            localInstanceId = localInstanceId[..48];

        return NormalizeSegment(localInstanceId);
    }

    private static string ToConsumerKindSegment(ChatBrokerConsumerKind consumerKind)
    {
        return consumerKind switch
        {
            ChatBrokerConsumerKind.Persistence => "persistence",
            ChatBrokerConsumerKind.Delivery => "delivery",
            _ => throw new ArgumentOutOfRangeException(nameof(consumerKind), consumerKind, "Loai consumer khong hop le.")
        };
    }

    private static string NormalizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
                continue;
            }

            builder.Append('-');
        }

        return builder.Length == 0
            ? throw new InvalidOperationException("Consumer identity segment khong hop le.")
            : builder.ToString();
    }
}
