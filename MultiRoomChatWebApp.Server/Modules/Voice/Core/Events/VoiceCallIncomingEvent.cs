using MediatR;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Events;

/// <summary>
/// Domain event yêu cầu gửi incoming call notification tới callee.
/// </summary>
public record VoiceCallIncomingEvent(
    Guid TargetUserId,
    VoiceCallIncomingDto Payload) : INotification;
