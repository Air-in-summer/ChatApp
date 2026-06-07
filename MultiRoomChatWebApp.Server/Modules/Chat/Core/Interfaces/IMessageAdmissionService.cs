using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

public interface IMessageAdmissionService
{
    Task<MessageAdmissionResult> AdmitAsync(
        SendMessageCommand request,
        CancellationToken cancellationToken);
}
