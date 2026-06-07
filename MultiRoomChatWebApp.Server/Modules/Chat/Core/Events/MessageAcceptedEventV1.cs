namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;

/// <summary>
/// Immutable event describing a message accepted by the admission path.
/// </summary>
public sealed class MessageAcceptedEventV1
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxContentLength = 4_000;
    public const int MaxAttachmentCount = 10;
    public const int MaxPayloadBytes = 64 * 1024;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Guid CorrelationId { get; init; }

    public string MessageId { get; init; } = string.Empty;

    public Guid ClientMessageId { get; init; }

    public DateTime AcceptedAtUtc { get; init; }

    public Guid RoomId { get; init; }

    public Guid SenderId { get; init; }

    public string Content { get; init; } = string.Empty;

    public List<Guid> MediaIds { get; init; } = [];

    public List<MessageAcceptedAttachmentV1> Attachments { get; init; } = [];
}
