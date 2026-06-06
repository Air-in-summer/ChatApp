using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Media.Services;

/// <summary>
/// Khoi tao bucket media khi ung dung start, khong thay doi behavior chat/avatar hien tai.
/// </summary>
public sealed class MediaStorageBootstrapService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaStorageBootstrapService> _logger;

    public MediaStorageBootstrapService(
        IServiceScopeFactory scopeFactory,
        ILogger<MediaStorageBootstrapService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
            await storageService.EnsureBucketsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Khong the khoi tao media storage. Kiem tra MinIO va duong dan D:\\ChatAppData\\minio.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
