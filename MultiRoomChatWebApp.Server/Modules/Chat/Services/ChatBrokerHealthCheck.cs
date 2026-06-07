using Microsoft.Extensions.Diagnostics.HealthChecks;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Kiem tra Redis broker rieng cua luong su kien chat.
/// </summary>
public sealed class ChatBrokerHealthCheck : IHealthCheck
{
    private readonly IChatBrokerConnection _brokerConnection;

    public ChatBrokerHealthCheck(IChatBrokerConnection brokerConnection)
    {
        _brokerConnection = brokerConnection;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await _brokerConnection.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy(
                $"Chat broker Redis ping thanh cong trong {latency.TotalMilliseconds:N0} ms.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Khong ket noi duoc Chat broker Redis.",
                ex);
        }
    }
}
