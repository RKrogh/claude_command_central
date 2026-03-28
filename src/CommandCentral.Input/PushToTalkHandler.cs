using CommandCentral.Core.Events;
using CommandCentral.Core.Services;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Input;

public sealed class PushToTalkHandler(
    IAudioInputManager audioInput,
    IKeystrokeInjector keystrokeInjector,
    IInstanceRegistry registry,
    IEventBus eventBus,
    IVirtualDesktopService virtualDesktop,
    InjectionBuffer injectionBuffer,
    DesktopNavigationContext navigationContext,
    ILogger<PushToTalkHandler> logger)
{
    private string? _activeInstanceId;
    private PttMode _activeMode;

    public async Task StartAsync(string? instanceId = null, CancellationToken ct = default)
    {
        if (audioInput.IsCapturing)
        {
            logger.LogWarning("PTT already active");
            return;
        }

        _activeInstanceId = instanceId ?? registry.SelectedInstanceId;
        _activeMode = PttMode.Normal;

        if (_activeInstanceId is null)
        {
            logger.LogWarning("No instance selected for PTT");
            return;
        }

        eventBus.Publish(new DaemonEvent(DaemonEventType.PttStarted, _activeInstanceId));
        await audioInput.StartCaptureAsync(ct);
        logger.LogInformation("PTT started for instance {Id}", _activeInstanceId);
    }

    public async Task StartFocusPttAsync(string instanceId, CancellationToken ct = default)
    {
        if (audioInput.IsCapturing)
        {
            logger.LogWarning("PTT already active");
            return;
        }

        _activeInstanceId = instanceId;
        _activeMode = PttMode.FocusPtt;

        var instance = registry.GetById(instanceId);
        if (instance is null)
        {
            logger.LogWarning("Instance {Id} not found for Focus PTT", instanceId);
            return;
        }

        // Save current context for quick-back
        if (virtualDesktop.IsAvailable)
        {
            var context = virtualDesktop.GetCurrentContext();
            navigationContext.Push(context);
        }

        // Switch to target desktop and focus window
        if (instance.WindowHandle != nint.Zero)
        {
            await virtualDesktop.SwitchToDesktopOfWindowAsync(instance.WindowHandle, ct);
        }

        eventBus.Publish(new DaemonEvent(DaemonEventType.PttStarted, _activeInstanceId));
        await audioInput.StartCaptureAsync(ct);
        logger.LogInformation("Focus PTT started for instance {Id}", _activeInstanceId);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        logger.LogInformation("PTT stop requested (isCapturing: {IsCapturing})", audioInput.IsCapturing);
        if (!audioInput.IsCapturing)
            return;

        var instanceId = _activeInstanceId;
        var mode = _activeMode;
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
            logger.LogWarning("Instance {Id} not found for injection", instanceId);
            return;
        }

        // Decide injection strategy based on mode and desktop
        if (mode == PttMode.FocusPtt || !virtualDesktop.IsAvailable)
        {
            // Focus PTT already switched desktops, or no VD support — inject directly
            await InjectDirectAsync(instance, text, ct);
        }
        else if (virtualDesktop.IsWindowOnCurrentDesktop(instance.WindowHandle))
        {
            // Same desktop — inject directly
            await InjectDirectAsync(instance, text, ct);
        }
        else
        {
            // Different desktop — buffer for later injection
            injectionBuffer.Buffer(instanceId, text);
        }
    }

    private async Task InjectDirectAsync(Core.Models.InstanceInfo instance, string text, CancellationToken ct)
    {
        await keystrokeInjector.InjectTextAsync(instance.WindowHandle, text, ct);

        eventBus.Publish(new InstanceEvent(
            Core.Events.InstanceEventType.ActivityLogged, instance.Id,
            Message: $"STT: \"{(text.Length > 50 ? text[..50] + "..." : text)}\""));

        logger.LogInformation("Injected STT text into instance {Id}", instance.Id);
    }
}
