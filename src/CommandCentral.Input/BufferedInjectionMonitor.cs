using CommandCentral.Core.Services;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Input;

/// <summary>
/// Polls for buffered text that can now be injected (target window on current desktop).
/// </summary>
public sealed class BufferedInjectionMonitor(
    InjectionBuffer buffer,
    IInstanceRegistry registry,
    IKeystrokeInjector injector,
    IVirtualDesktopService virtualDesktop,
    ILogger<BufferedInjectionMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!virtualDesktop.IsAvailable)
        {
            logger.LogInformation("Virtual desktop service unavailable — buffered injection monitor disabled");
            return;
        }

        logger.LogInformation("Buffered injection monitor started (polling every {Interval}ms)", PollInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                await FlushReadyBuffersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in buffered injection monitor");
            }
        }
    }

    private async Task FlushReadyBuffersAsync(CancellationToken ct)
    {
        var pendingIds = buffer.GetPendingInstanceIds();
        if (pendingIds.Count == 0)
            return;

        foreach (var instanceId in pendingIds)
        {
            var instance = registry.GetById(instanceId);
            if (instance is null || instance.WindowHandle == nint.Zero)
                continue;

            if (!virtualDesktop.IsWindowOnCurrentDesktop(instance.WindowHandle))
                continue;

            var text = buffer.TryConsume(instanceId);
            if (text is null)
                continue;

            logger.LogInformation("Injecting buffered text into instance {Id} ({Length} chars)", instanceId, text.Length);
            await injector.InjectTextAsync(instance.WindowHandle, text, ct);
        }
    }
}
