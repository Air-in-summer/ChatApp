using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Tap trung cac quy tac doc phong, tac gia va quyen quan tri message.
/// </summary>
public interface IMessageMutationPermissionService
{
    /// <summary>
    /// Xac nhan user con quyen doc phong va tra ve ngu canh phong.
    /// </summary>
    Task<MessageMutationRoomContext> EnsureCanReadRoomAsync(
        Guid roomId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiem tra user co phai tac gia tin nhan hay khong.
    /// </summary>
    bool IsAuthor(Message message, Guid userId);

    /// <summary>
    /// Kiem tra user co quyen Owner/Admin trong group cua phong hay khong.
    /// </summary>
    Task<bool> IsGroupRoomModeratorAsync(
        MessageMutationRoomContext roomContext,
        Guid userId,
        CancellationToken cancellationToken = default);
}
