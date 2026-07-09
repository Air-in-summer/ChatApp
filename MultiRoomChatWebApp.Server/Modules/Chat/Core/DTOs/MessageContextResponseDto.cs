using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Ket qua lay cua so timeline quanh mot tin nhan dich.
/// </summary>
public sealed class MessageContextResponseDto
{
    public string TargetMessageId { get; init; } = string.Empty;

    public IReadOnlyList<Message> Messages { get; init; } = [];

    public bool HasMoreBefore { get; init; }

    public bool HasMoreAfter { get; init; }

    public string? BeforeCursor { get; init; }

    public string? AfterCursor { get; init; }
}
