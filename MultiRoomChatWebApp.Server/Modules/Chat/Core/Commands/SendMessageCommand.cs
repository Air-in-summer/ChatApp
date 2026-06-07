using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;

/// <summary>
/// Gói dữ liệu chuyển tiếp (DTO) từ SignalR Hub đẩy vào hàng chờ MediatR.
/// </summary>
public class SendMessageCommand : IRequest<MessageAcceptedResult>
{
    public Guid RoomId { get; set; }
    
    public Guid SenderId { get; set; }
    
    public string Content { get; set; } = string.Empty;

    public List<Guid> MediaIds { get; set; } = [];

    /// <summary>
    /// UUID do frontend tạo một lần cho một thao tác gửi logic và giữ nguyên khi retry.
    /// </summary>
    public Guid ClientMessageId { get; set; }
}
