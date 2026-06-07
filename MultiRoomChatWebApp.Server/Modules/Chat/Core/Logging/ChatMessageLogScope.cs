using System.Text.Json;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Logging;

/// <summary>
/// Tao structured logging scope thong nhat cho toan bo vong doi message.
/// </summary>
public static class ChatMessageLogScope
{
    public static IDisposable? Begin(
        ILogger logger,
        string? messageId,
        Guid? clientMessageId,
        string? streamId,
        Guid? roomId,
        Guid? senderId,
        string group,
        string consumer,
        int attempt,
        Guid? correlationId)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = messageId ?? string.Empty,
            ["ClientMessageId"] = clientMessageId,
            ["StreamId"] = streamId ?? string.Empty,
            ["RoomId"] = roomId,
            ["SenderId"] = senderId,
            ["Group"] = group,
            ["Consumer"] = consumer,
            ["Attempt"] = attempt,
            ["CorrelationId"] = correlationId
        });
    }

    public static IDisposable? BeginForEntry(
        ILogger logger,
        string streamId,
        string group,
        string consumer,
        int attempt,
        string? payload)
    {
        var acceptedEvent = TryReadEvent(payload);
        return Begin(
            logger,
            acceptedEvent?.MessageId,
            acceptedEvent?.ClientMessageId,
            streamId,
            acceptedEvent?.RoomId,
            acceptedEvent?.SenderId,
            group,
            consumer,
            attempt,
            acceptedEvent?.CorrelationId);
    }

    private static MessageAcceptedEventV1? TryReadEvent(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MessageAcceptedEventV1>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
