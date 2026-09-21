using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Ngữ cảnh (Context) đầu vào để kiểm duyệt tin nhắn.
/// </summary>
/// <param name="RoomId">Phòng chat nơi tin nhắn được gửi.</param>
/// <param name="SenderId">Người gửi tin nhắn.</param>
/// <param name="ClientMessageId">ID tạm do client cấp để đối chiếu.</param>
/// <param name="Content">Nội dung văn bản của tin nhắn.</param>
/// <param name="MediaIds">Danh sách ID các tệp đính kèm (ảnh, video, file).</param>
/// <param name="GroupId">ID của Server/Group (null nếu là chat cá nhân).</param>
/// <param name="IsPrivateRoom">Cờ xác định đây là phòng bí mật hay công khai.</param>
/// <param name="RoomType">Phân loại phòng chat (Direct Message, Text, Voice).</param>
public sealed record MessageAdmissionContext(
    Guid RoomId,
    Guid SenderId,
    Guid ClientMessageId,
    string Content,
    IReadOnlyList<Guid> MediaIds,
    Guid? GroupId,
    bool IsPrivateRoom,
    RoomType RoomType);
