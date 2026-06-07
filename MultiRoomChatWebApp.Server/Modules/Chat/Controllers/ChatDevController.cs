using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Exceptions;

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

    [HttpPost("send")]
    public async Task<IActionResult> TestSendMessage([FromBody] SendMessageRequestDto request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var currentUserId))
        {
            return Unauthorized();
        }

        var command = new SendMessageCommand
        {
            RoomId = request.RoomId,
            SenderId = currentUserId,
            Content = request.Content,
            ClientMessageId = request.ClientMessageId,
            MediaIds = request.MediaIds
        };

        try
        {
            return Ok(await _mediator.Send(command));
        }
        catch (MessageAdmissionException ex)
        {
            return StatusCode(
                ToHttpStatusCode(ex.Error),
                new
                {
                    code = ex.Error.Code,
                    message = ex.Error.ClientMessage,
                    retryable = ex.Error.IsRetryable
                });
        }
    }

    private static int ToHttpStatusCode(MessageAdmissionError error)
    {
        return error.Kind switch
        {
            MessageAdmissionErrorKind.Validation => StatusCodes.Status400BadRequest,
            MessageAdmissionErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            MessageAdmissionErrorKind.Conflict => StatusCodes.Status409Conflict,
            MessageAdmissionErrorKind.BrokerUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
    }
}

public class SendMessageRequestDto
{
    public Guid RoomId { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid ClientMessageId { get; set; }
    public List<Guid> MediaIds { get; set; } = [];
}
