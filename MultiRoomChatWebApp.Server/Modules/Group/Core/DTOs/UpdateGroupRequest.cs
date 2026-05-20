namespace MultiRoomChatWebApp.Server.Modules.Group.Core.DTOs;

public class UpdateGroupRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
}
