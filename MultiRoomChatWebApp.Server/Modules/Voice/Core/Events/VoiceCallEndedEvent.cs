using MediatR;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Events;

/// <summary>
/// Domain event yêu cầu báo cho các participant còn lại biết DM call đã kết thúc.
/// </summary>
public record VoiceCallEndedEvent(
    IEnumerable<Guid> TargetUserIds,
    VoiceCallStatusChangedDto Payload) : INotification;
