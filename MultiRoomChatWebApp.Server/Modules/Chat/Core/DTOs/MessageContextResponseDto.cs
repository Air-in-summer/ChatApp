using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Dữ liệu ngữ cảnh xung quanh một tin nhắn (dùng khi tìm kiếm).
/// </summary>
public sealed class MessageContextResponseDto
{
    /// <summary>
    /// Định danh của tin nhắn đích được tìm kiếm.
    /// </summary>
    public string TargetMessageId { get; init; } = string.Empty;

    /// <summary>
    /// Danh sách các tin nhắn xung quanh (bao gồm tin nhắn đích, tin cũ hơn và tin mới hơn).
    /// </summary>
    public IReadOnlyList<Message> Messages { get; init; } = [];

    /// <summary>
    /// Đánh dấu xem có còn tin nhắn nào cũ hơn trong lịch sử không.
    /// </summary>
    public bool HasMoreBefore { get; init; }

    /// <summary>
    /// Đánh dấu xem có còn tin nhắn nào mới hơn trong lịch sử không.
    /// </summary>
    public bool HasMoreAfter { get; init; }

    /// <summary>
    /// Con trỏ (Cursor) dùng để phân trang ngược (lấy tin nhắn cũ hơn).
    /// </summary>
    public string? BeforeCursor { get; init; }

    /// <summary>
    /// Con trỏ (Cursor) dùng để phân trang xuôi (lấy tin nhắn mới hơn).
    /// </summary>
    public string? AfterCursor { get; init; }
}
