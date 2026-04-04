namespace MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

public class UserCacheDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
