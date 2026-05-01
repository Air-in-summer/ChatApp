namespace MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
}
