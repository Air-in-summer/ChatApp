namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

/// <summary>
/// Định danh ổn định của một lần gửi logic, dùng lại khi client gửi lại cùng clientMessageId.
/// </summary>
public sealed record MessageIdentity(
    string MessageId,
    DateTime AcceptedAtUtc);
