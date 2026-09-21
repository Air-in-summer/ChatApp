namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Trạng thái thử lại (Retry) của một tin nhắn.
/// </summary>
/// <param name="Attempt">Số lần hiện tại đã thử.</param>
/// <param name="NextRetryAtUtc">Thời điểm (UTC) lên lịch cho lần thử kế tiếp.</param>
public sealed record ChatBrokerRetryState(
    int Attempt,
    DateTime NextRetryAtUtc);
