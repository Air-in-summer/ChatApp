using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Chi tiết lỗi khi kiểm duyệt tin nhắn thất bại.
/// </summary>
/// <param name="Kind">Phân loại lỗi (Kiểm tra hợp lệ, Cấm truy cập, Xung đột, Mất kết nối Broker).</param>
/// <param name="Code">Mã lỗi máy tính (machine-readable) để client dễ xử lý (ví dụ: msg_too_long).</param>
/// <param name="ClientMessage">Thông báo lỗi thân thiện (human-readable) để hiển thị cho người dùng.</param>
/// <param name="IsRetryable">Xác định xem client có nên thử lại request này hay không (true đối với lỗi rớt mạng/broker, false nếu lỗi logic/quyền).</param>
public sealed record MessageAdmissionError(
    MessageAdmissionErrorKind Kind,
    string Code,
    string ClientMessage,
    bool IsRetryable)
{
    public static MessageAdmissionError Validation(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.Validation, code, clientMessage, IsRetryable: false);

    public static MessageAdmissionError Forbidden(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.Forbidden, code, clientMessage, IsRetryable: false);

    public static MessageAdmissionError Conflict(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.Conflict, code, clientMessage, IsRetryable: false);

    public static MessageAdmissionError BrokerUnavailable(string code, string clientMessage)
        => new(MessageAdmissionErrorKind.BrokerUnavailable, code, clientMessage, IsRetryable: true);
}
