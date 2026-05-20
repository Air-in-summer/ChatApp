using Livekit.Server.Sdk.Dotnet;
using Microsoft.AspNetCore.Mvc;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Controllers;

/// <summary>
/// Controller nhan webhook lifecycle tu LiveKit.
/// </summary>
/// <remarks>
/// Endpoint nay khong dung JWT cua user. LiveKit gui chu ky trong header Authorization,
/// backend verify bang WebhookReceiver voi ApiKey/ApiSecret trung voi livekit.yaml.
/// </remarks>
[ApiController]
[Route("api/v1/voice/livekit")]
public class LiveKitWebhookController : ControllerBase
{
    private const string ParticipantLeftEvent = "participant_left";
    private const string ParticipantConnectionAbortedEvent = "participant_connection_aborted";

    private readonly IConfiguration _configuration;
    private readonly IVoiceSessionService _voiceSessionService;
    private readonly ILogger<LiveKitWebhookController> _logger;

    public LiveKitWebhookController(
        IConfiguration configuration,
        IVoiceSessionService voiceSessionService,
        ILogger<LiveKitWebhookController> logger)
    {
        _configuration = configuration;
        _voiceSessionService = voiceSessionService;
        _logger = logger;
    }

    /// <summary>
    /// [POST] /api/v1/voice/livekit/webhook - Nhan event tu LiveKit server.
    /// </summary>
    /// <returns>200 neu webhook hop le va da xu ly/no-op; 401 neu verify chu ky that bai.</returns>
    /// <remarks>
    /// Luong xu ly:
    /// 1. Doc raw request body vi LiveKit SDK can chuoi goc de verify checksum.
    /// 2. Verify header Authorization bang WebhookReceiver.
    /// 3. Chi xu ly participant_left/participant_connection_aborted cho DirectCall room `call:{sessionId}`.
    /// 4. Chuyen lifecycle ve VoiceSessionService de reuse rule Missed/Ended hien co.
    /// </remarks>
    [HttpPost("webhook")]
    [Consumes("application/webhook+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var authorizationHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return Unauthorized("Missing LiveKit webhook authorization header.");
        }

        var postData = await new StreamReader(Request.Body).ReadToEndAsync(cancellationToken);

        WebhookEvent webhookEvent;
        try
        {
            var liveKitSection = _configuration.GetSection("LiveKit");
            var apiKey = liveKitSection["ApiKey"]
                ?? throw new InvalidOperationException("Missing LiveKit:ApiKey configuration.");
            var apiSecret = liveKitSection["ApiSecret"]
                ?? throw new InvalidOperationException("Missing LiveKit:ApiSecret configuration.");

            var webhookReceiver = new WebhookReceiver(apiKey, apiSecret);
            webhookEvent = webhookReceiver.Receive(postData, authorizationHeader);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LiveKit webhook verify failed.");
            return Unauthorized("Invalid LiveKit webhook signature.");
        }

        if (webhookEvent.Event is not ParticipantLeftEvent and not ParticipantConnectionAbortedEvent)
        {
            return Ok();
        }

        var liveKitRoomName = webhookEvent.Room?.Name;
        var participantIdentity = webhookEvent.Participant?.Identity;
        if (string.IsNullOrWhiteSpace(liveKitRoomName) || string.IsNullOrWhiteSpace(participantIdentity))
        {
            _logger.LogWarning(
                "Bo qua LiveKit webhook {Event} vi thieu room hoac participant identity.",
                webhookEvent.Event);
            return Ok();
        }

        await _voiceSessionService.HandleLiveKitParticipantLeftAsync(
            liveKitRoomName,
            participantIdentity,
            cancellationToken);

        return Ok();
    }
}
