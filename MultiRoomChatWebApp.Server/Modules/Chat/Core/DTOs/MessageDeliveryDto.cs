using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Dữ liệu tin nhắn được phát tới client qua kênh thời gian thực.
/// </summary>
public sealed class MessageDeliveryDto
{
    public string Id { get; init; } = string.Empty;

    public Guid ClientMessageId { get; init; }

    public Guid RoomId { get; init; }

    public Guid SenderId { get; init; }

    public MessageType Type { get; init; }

    public string Content { get; init; } = string.Empty;

    public List<Attachment>? Attachments { get; init; }

    public MessageStatus Status { get; init; }

    public DateTime AcceptedAtUtc { get; init; }

    public DateTime CreatedAt { get; init; }
}
