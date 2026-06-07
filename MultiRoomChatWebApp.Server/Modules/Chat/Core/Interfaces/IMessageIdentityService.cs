using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Cấp và lưu ánh xạ định danh ổn định cho một lần gửi logic.
/// </summary>
public interface IMessageIdentityService
{
    /// <summary>
    /// Trả lại cùng messageId khi sender gửi lại cùng clientMessageId trong cửa sổ idempotency.
    /// </summary>
    Task<MessageIdentity> ResolveAsync(Guid senderId, Guid clientMessageId);
}
