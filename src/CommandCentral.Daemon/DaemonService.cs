using CommandCentral.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Daemon;

public sealed class DaemonService(
    HotkeyManager hotkeyManager,
    ILogger<DaemonService> logger) : IHostedService
{
    private Task? _hookTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Command Central daemon service starting");
        // RunAsync runs the global hook event loop — it never completes until disposed.
        // Fire-and-forget so we don't block the web server startup pipeline.
        _hookTask = hotkeyManager.StartAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Command Central daemon service stopping");
        hotkeyManager.Stop();
        return Task.CompletedTask;
    }
}
