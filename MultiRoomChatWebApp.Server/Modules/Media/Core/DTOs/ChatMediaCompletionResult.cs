namespace MultiRoomChatWebApp.Server.Modules.Media.Core.DTOs;

public sealed record ChatMediaCompletionResult(
    bool IsSuccess,
    bool IsNoOp,
    string? ConflictCode,
    string? ConflictReason)
{
    public static ChatMediaCompletionResult Completed(bool isNoOp)
        => new(true, isNoOp, null, null);

    public static ChatMediaCompletionResult Conflict(string code, string reason)
        => new(false, false, code, reason);
}
