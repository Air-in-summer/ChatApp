using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;

/// <summary>
/// Gói dữ liệu chuyển tiếp (DTO) từ SignalR Hub đẩy vào hàng chờ MediatR.
/// </summary>
public class SendMessageCommand : IRequest<bool>
{
    public Guid RoomId { get; set; }
    
    public Guid SenderId { get; set; }
    
    public string Content { get; set; } = string.Empty;
    
    public List<Attachment>? Attachments { get; set; }

    /// <summary>
    /// ID tạm do Frontend tự gán (ví dụ: "temp-1745808000000").
    /// Worker sẽ dùng để gọi lại MessageStatusUpdated đúng tin tạm sau khi MongoDB Insert thành công.
    /// </summary>
    public string TempId { get; set; } = string.Empty;
}
