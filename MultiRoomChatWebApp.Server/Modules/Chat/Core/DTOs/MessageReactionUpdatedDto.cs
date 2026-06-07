namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Snapshot reaction moi nhat cua tin nhan dung cho dong bo realtime.
/// </summary>
public sealed record MessageReactionUpdatedDto(
    Guid RoomId,
    string MessageId,
    IReadOnlyList<MessageReactionDto> Reactions);
