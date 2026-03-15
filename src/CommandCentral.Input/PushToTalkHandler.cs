using CommandCentral.Core.Events;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Input;

public sealed class PushToTalkHandler(
    IAudioInputManager audioInput,
    IKeystrokeInjector keystrokeInjector,
    IInstanceRegistry registry,
    IEventBus eventBus,
    ILogger<PushToTalkHandler> logger)
{
    private string? _activeInstanceId;

    public async Task StartAsync(string? instanceId = null, CancellationToken ct = default)
    {
        if (audioInput.IsCapturing)
        {
            logger.LogWarning("PTT already active");
            return;
        }

        _activeInstanceId = instanceId ?? registry.SelectedInstanceId;
        if (_activeInstanceId is null)
        {
            logger.LogWarning("No instance selected for PTT");
            return;
        }

        eventBus.Publish(new DaemonEvent(DaemonEventType.PttStarted, _activeInstanceId));
        await audioInput.StartCaptureAsync(ct);
        logger.LogInformation("PTT started for instance {Id}", _activeInstanceId);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!audioInput.IsCapturing)
            return;

        var instanceId = _activeInstanceId;
        _activeInstanceId = null;

        var text = await audioInput.StopCaptureAndTranscribeAsync(ct);
        eventBus.Publish(new DaemonEvent(DaemonEventType.PttStopped, instanceId));

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogDebug("PTT stopped — no text recognized");
            return;
        }

        eventBus.Publish(new DaemonEvent(DaemonEventType.SttResult, instanceId, text));

        if (instanceId is null)
            return;

        var instance = registry.GetById(instanceId);
        if (instance is null)
        {
            logger.LogWarning("Instance {Id} not found for keystroke injection", instanceId);
            return;
        }

        await keystrokeInjector.InjectTextAsync(instance.WindowHandle, text, ct);

        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, instanceId,
            Message: $"STT: \"{(text.Length > 50 ? text[..50] + "..." : text)}\""));

        logger.LogInformation("Injected STT text into instance {Id}", instanceId);
    }
}
