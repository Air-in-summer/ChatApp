namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;

public sealed class MessageSearchResultDto
{
    public string MessageId { get; set; } = string.Empty;
    public Guid RoomId { get; set; }
    public Guid SenderId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class MessageSearchResponseDto
{
    public IReadOnlyList<MessageSearchResultDto> Items { get; set; } = Array.Empty<MessageSearchResultDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
