using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

/// <summary>
/// Định nghĩa các methods từ Backend gọi ngược (Broadcast) về Frontend qua Websocket.
/// Giúp loại bỏ hoàn toàn việc xài "Magic String" khi code C#.
/// </summary>
public interface IChatClient
{
    /// <summary>
    /// Gửi thông báo người dùng đổi trạng thái cắm cờ Online.
    /// </summary>
    Task UserIsOnline(Guid userId);

    /// <summary>
    /// Gửi thông báo người dùng tắt hoàn toàn app/web - Offline.
    /// </summary>
    Task UserIsOffline(Guid userId);

    /// <summary>
    /// Frontend sẽ lắng nghe sự kiện này để in chử ra màn hình.
    /// </summary>
    Task ReceiveMessage(object message); 

    /// <summary>
    /// Nhận thông báo một user đang gõ phím.
    /// </summary>
    Task ReceiveTyping(Guid userId, Guid roomId);

    /// <summary>
    /// Nhận thông báo một user đã ngừng gõ phím.
    /// </summary>
    Task ReceiveTypingStopped(Guid userId, Guid roomId);

    /// <summary>
    /// Nhận thông báo một user đã xem tin nhắn đến ID nào trong phòng nào.
    /// roomId bắt buộc phải kèm theo để Frontend biết cập nhật đúng phòng.
    /// </summary>
    Task ReceiveReadReceipt(Guid userId, Guid roomId, string lastReadMessageId);

    /// <summary>
    /// Notify người gửi rằng tin nhắn của họ đã được Worker lưu thành công.
    /// Payload: tempId để FE tìm đúng tin tạm cần update.
    /// </summary>
    Task MessageStatusUpdated(string tempId, string finalMessageId, string status);

    // === NOTIFICATION SIGNALS ===

    /// <summary>
    /// Báo cho các thành viên trong Group (trừ người tạo) biết có phòng mới vừa được tạo.
    /// </summary>
    Task GroupRoomsUpdated(Guid groupId);

    /// <summary>
    /// Báo cho thành viên biết họ vừa bị Kick khỏi Group.
    /// </summary>
    Task YouWereKicked(Guid groupId, string groupName);

    /// <summary>
    /// Báo cho các thành viên biết Group đã bị giải tán.
    /// </summary>
    Task GroupDeleted(Guid groupId, string groupName);

    /// <summary>
    /// Báo cho thành viên biết Role của họ vừa bị thay đổi (bổ nhiệm/bãi miễn Admin).
    /// </summary>
    Task MemberRoleChanged(Guid groupId, Guid userId, string newRole);

    /// <summary>
    /// Báo cho callee biết có DM call mới đang gọi tới.
    /// </summary>
    Task VoiceCallIncoming(VoiceCallIncomingDto payload);

    /// <summary>
    /// Báo cho caller biết DM call đã được accept.
    /// </summary>
    Task VoiceCallAccepted(VoiceCallStatusChangedDto payload);

    /// <summary>
    /// Báo cho caller biết DM call đã bị decline.
    /// </summary>
    Task VoiceCallDeclined(VoiceCallStatusChangedDto payload);

    /// <summary>
    /// Báo cho participant còn lại biết DM call đã kết thúc.
    /// </summary>
    Task VoiceCallEnded(VoiceCallStatusChangedDto payload);
}
