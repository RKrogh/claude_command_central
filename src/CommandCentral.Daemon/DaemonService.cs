using CommandCentral.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Daemon;

public sealed class DaemonService(
    HotkeyManager hotkeyManager,
    ILogger<DaemonService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Command Central daemon service starting");
        return hotkeyManager.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Command Central daemon service stopping");
        hotkeyManager.Stop();
        return Task.CompletedTask;
    }
}
