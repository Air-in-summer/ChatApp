using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Quyết định thử lại (Retry) từ Message Broker.
/// </summary>
/// <param name="Attempt">Số lần đã thử (bao gồm cả lần chuẩn bị thực hiện).</param>
/// <param name="NextRetryAtUtc">Thời điểm dự kiến sẽ thực hiện lần thử lại tiếp theo (theo giờ UTC).</param>
/// <param name="IsDue">Cờ đánh dấu liệu đã đến thời điểm cần thực hiện thử lại ngay lập tức chưa.</param>
/// <param name="IsExhausted">Cờ đánh dấu liệu đã hết số lần thử lại cho phép (vượt quá giới hạn cấu hình) hay chưa.</param>
/// <param name="FailureKind">Phân loại lỗi dẫn đến việc phải thử lại (ví dụ: lỗi mạng, lỗi database).</param>
/// <param name="ErrorCode">Mã lỗi chi tiết từ hệ thống hoặc exception.</param>
/// <param name="LastError">Thông điệp lỗi cuối cùng gặp phải.</param>
public sealed record ChatBrokerRetryDecision(
    int Attempt,
    DateTime? NextRetryAtUtc,
    bool IsDue,
    bool IsExhausted,
    ChatBrokerEntryFailureKind? FailureKind,
    string? ErrorCode,
    string? LastError);
