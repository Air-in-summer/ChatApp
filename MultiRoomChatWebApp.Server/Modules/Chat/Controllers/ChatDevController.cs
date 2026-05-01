using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using System.Security.Claims;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Controllers;

[ApiController]
[Route("api/chat/dev-test")]
[Authorize]
public class ChatDevController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChatDevController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Giả lập việc bắn tin nhắn từ Websocket để dễ dàng test bằng ThunderClient/Postman.
    /// Giúp dev theo dõi Background Worker hứng dữ liệu trên log console ra sao.
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> TestSendMessage([FromBody] SendMessageRequestDto request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out Guid currentUserId)) return Unauthorized();

        var command = new SendMessageCommand
        {
            RoomId = request.RoomId,
            SenderId = currentUserId,
            Content = request.Content
        };

        // Kích hoạt MediatR kịch bản chính (Handler)
        bool success = await _mediator.Send(command);

        if (!success) 
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Bạn không có quyền chat trong phòng này (Bị chặn bởi Redis O(1))." });

        return Ok(new { message = "Gói lệnh đã được quăng vào Redis Pub/Sub Queue thành công! (Fire and Forget)" });
    }
}

public class SendMessageRequestDto
{
    public Guid RoomId { get; set; }
    public string Content { get; set; } = string.Empty;
}
