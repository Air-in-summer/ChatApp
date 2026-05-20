using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

/// <summary>
/// Service quản lý lifecycle của VoiceSession cho DM call.
/// Phase đầu không dùng service này cho voice room channel.
/// </summary>
public interface IVoiceSessionService
{
    /// <summary>
    /// Bắt đầu một DM call từ DirectMessage room.
    /// Caller được join ngay, callee ở trạng thái Invited.
    /// </summary>
    Task<VoiceSessionTokenResponseDto> StartDirectCallAsync(
        Guid dmRoomId,
        Guid callerUserId,
        string callerDisplayName);

    /// <summary>
    /// Accept một DM call đang ringing và trả token để callee vào LiveKit room.
    /// </summary>
    Task<VoiceSessionTokenResponseDto> AcceptAsync(
        Guid sessionId,
        Guid userId,
        string displayName);

    /// <summary>
    /// Decline một DM call khi current user đang ở trạng thái Invited.
    /// </summary>
    Task<VoiceSessionResponseDto> DeclineAsync(
        Guid sessionId,
        Guid userId);

    /// <summary>
    /// Lấy lại token cho reconnect/refresh nếu user vẫn là participant hợp lệ.
    /// </summary>
    Task<VoiceSessionTokenResponseDto> GetTokenAsync(
        Guid sessionId,
        Guid userId,
        string displayName);

    /// <summary>
    /// Rời khỏi DM call. Nếu không còn participant active thì session chuyển sang Ended.
    /// </summary>
    Task<VoiceSessionResponseDto> LeaveAsync(
        Guid sessionId,
        Guid userId);

    /// <summary>
    /// Mark cac DM call dang Ringing qua han thanh Missed.
    /// </summary>
    Task<int> MarkExpiredRingingCallsAsMissedAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Xu ly callback tu LiveKit khi participant roi room hoac mat ket noi bat thuong.
    /// </summary>
    Task HandleLiveKitParticipantLeftAsync(
        string liveKitRoomName,
        string participantIdentity,
        CancellationToken cancellationToken);
}
