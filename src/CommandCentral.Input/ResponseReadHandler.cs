using CommandCentral.Core.Events;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Input;

/// <summary>
/// Reads an instance's last assistant response aloud, toggle-style:
/// the same hotkey starts reading and stops an in-progress read.
/// Triggering a read for a different instance stops the current one
/// and starts the new one.
/// </summary>
public sealed class ResponseReadHandler(
    IInstanceRegistry registry,
    ITtsNotifier ttsNotifier,
    IEventBus eventBus,
    ILogger<ResponseReadHandler> logger)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _activeCts;
    private string? _activeInstanceId;

    public bool IsReading
    {
        get { lock (_gate) return _activeCts is not null; }
    }

    public async Task ToggleReadAsync(string? instanceId = null, CancellationToken ct = default)
    {
        var targetId = instanceId ?? registry.SelectedInstanceId;
        if (targetId is null)
        {
            logger.LogWarning("Response read requested but no instance is selected");
            return;
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_activeCts is not null)
            {
                var stoppingSame = _activeInstanceId == targetId;
                _activeCts.Cancel();
                _activeCts = null;
                _activeInstanceId = null;

                if (stoppingSame)
                {
                    logger.LogDebug("Response read toggled off for instance {Id}", targetId);
                    return;
                }
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _activeCts = cts;
            _activeInstanceId = targetId;
        }

        var instance = registry.GetById(targetId);
        var text = instance?.LastAssistantMessage;

        if (instance is null || string.IsNullOrWhiteSpace(text))
        {
            logger.LogInformation("No response available to read for instance {Id}", targetId);
            Cleanup(cts);
            cts.Dispose();
            return;
        }

        eventBus.Publish(new DaemonEvent(
            DaemonEventType.TtsStarted, targetId, $"Reading response ({text.Length} chars)"));
        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, targetId, Message: "Reading response aloud"));

        try
        {
            await ttsNotifier.ReadResponseAsync(text, targetId, cts.Token);
        }
        finally
        {
            Cleanup(cts);
            cts.Dispose();
            eventBus.Publish(new DaemonEvent(DaemonEventType.TtsStopped, targetId));
        }
    }

    private void Cleanup(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeCts, cts))
            {
                _activeCts = null;
                _activeInstanceId = null;
            }
        }
    }
}
