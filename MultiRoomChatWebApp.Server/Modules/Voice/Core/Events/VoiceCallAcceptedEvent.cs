using MediatR;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Events;

/// <summary>
/// Domain event yêu cầu báo cho caller biết DM call đã được accept.
/// </summary>
public record VoiceCallAcceptedEvent(
    Guid TargetUserId,
    VoiceCallStatusChangedDto Payload) : INotification;
