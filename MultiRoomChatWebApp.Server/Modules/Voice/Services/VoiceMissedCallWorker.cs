using MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Services;

/// <summary>
/// Background worker dong cac DM call bi treo o trang thai Ringing qua timeout.
/// </summary>
public class VoiceMissedCallWorker : BackgroundService
{
    private const int DefaultRingingTimeoutSeconds = 60;
    private const int DefaultScanIntervalSeconds = 5;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoiceMissedCallWorker> _logger;
    private readonly TimeSpan _ringingTimeout;
    private readonly TimeSpan _scanInterval;

    public VoiceMissedCallWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<VoiceMissedCallWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _ringingTimeout = TimeSpan.FromSeconds(Math.Max(
            1,
            configuration.GetValue<int?>("Voice:DirectCallRingingTimeoutSeconds")
                ?? DefaultRingingTimeoutSeconds));
        _scanInterval = TimeSpan.FromSeconds(Math.Max(
            1,
            configuration.GetValue<int?>("Voice:MissedCallScanIntervalSeconds")
                ?? DefaultScanIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "VoiceMissedCallWorker started. RingingTimeout={RingingTimeoutSeconds}s, ScanInterval={ScanIntervalSeconds}s",
            _ringingTimeout.TotalSeconds,
            _scanInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var voiceSessionService = scope.ServiceProvider.GetRequiredService<IVoiceSessionService>();
                var cutoffUtc = DateTime.UtcNow.Subtract(_ringingTimeout);

                await voiceSessionService.MarkExpiredRingingCallsAsMissedAsync(
                    cutoffUtc,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while marking expired ringing calls as missed.");
            }

            try
            {
                await Task.Delay(_scanInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
