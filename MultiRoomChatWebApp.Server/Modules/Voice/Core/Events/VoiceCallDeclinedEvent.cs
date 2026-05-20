using MediatR;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Events;

/// <summary>
/// Domain event yêu cầu báo cho caller biết DM call đã bị decline.
/// </summary>
public record VoiceCallDeclinedEvent(
    Guid TargetUserId,
    VoiceCallStatusChangedDto Payload) : INotification;
