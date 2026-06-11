using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using CommandCentral.Input;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Daemon;

public sealed class Orchestrator(
    IInstanceRegistry registry,
    IEventBus eventBus,
    IWindowBindingService windowBinding,
    IVirtualDesktopService virtualDesktopService,
    ITtsNotifier ttsNotifier,
    IPersonalityManager personalityManager,
    IKeystrokeInjector keystrokeInjector,
    INotificationCacheWarmer notificationCacheWarmer,
    ILogger<Orchestrator> logger) : IOrchestrator
{
    public async Task HandleSessionStartAsync(HookPayload payload, string? windowMarker = null, string? wtSession = null, CancellationToken ct = default)
    {
        if (payload.SessionId is null)
        {
            logger.LogWarning("SessionStart hook received without session_id");
            return;
        }

        var existing = registry.GetBySessionId(payload.SessionId);
        if (existing is not null)
        {
            logger.LogInformation("Session {SessionId} already registered as instance {Id}", payload.SessionId, existing.Id);
            return;
        }

        var instance = registry.Register(payload.SessionId, payload.Cwd);
        instance.WtSession = wtSession;

        // Resolve the terminal window for this session (marker best effort,
        // then foreground claim). Refined later on every prompt submit.
        var windowHandle = await windowBinding.BindOnSessionStartAsync(instance, windowMarker, ct);

        RefreshDesktopId(instance);

        logger.LogInformation("Registered instance {Id} for session {SessionId} (project: {Project}, window: 0x{Handle:X}, source: {Source}, wt: {WtSession})",
            instance.Id, payload.SessionId, instance.ProjectName ?? "unknown", windowHandle, instance.WindowBindingSource, wtSession ?? "none");

        // Inject personality and play greeting independently of the hook timeout.
        // These can take longer than the hook's cancellation token allows.
        _ = Task.Run(async () =>
        {
            try
            {
                await InjectPersonalityAsync(instance, CancellationToken.None);
                await ttsNotifier.NotifyInstanceReadyAsync(instance.Id);
                await WarmupNotificationCacheAsync(instance.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Post-registration tasks failed for slot {Slot}", instance.Id);
            }
        });
    }

    public async Task HandleStopAsync(HookPayload payload, CancellationToken ct = default)
    {
        if (payload.SessionId is null) return;

        var instance = registry.GetBySessionId(payload.SessionId);
        if (instance is null)
        {
            logger.LogDebug("Stop hook for unknown session {SessionId}", payload.SessionId);
            return;
        }

        instance.LastAssistantMessage = payload.LastAssistantMessage;
        registry.UpdateState(payload.SessionId, InstanceState.WaitingForInput);

        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, instance.Id,
            Message: "Response complete"));

        logger.LogInformation("Instance {Id} response complete", instance.Id);

        // Play done notification (fire-and-forget, don't block the hook response)
        _ = ttsNotifier.NotifyDoneAsync(instance.Id, ct);
    }

    public Task HandleNotificationAsync(HookPayload payload, CancellationToken ct = default)
    {
        if (payload.SessionId is null) return Task.CompletedTask;

        var instance = registry.GetBySessionId(payload.SessionId);
        if (instance is null) return Task.CompletedTask;

        registry.UpdateState(payload.SessionId, InstanceState.Idle);

        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, instance.Id,
            Message: "Notification: idle"));

        return Task.CompletedTask;
    }

    public async Task HandlePromptSubmitAsync(HookPayload payload, string? wtSession = null, CancellationToken ct = default)
    {
        if (payload.SessionId is null) return;

        var instance = registry.GetBySessionId(payload.SessionId);
        if (instance is null) return;

        if (wtSession is not null)
            instance.WtSession = wtSession;

        registry.UpdateState(payload.SessionId, InstanceState.Busy);

        // Strongest window signal available: the user just submitted a prompt
        // in this instance's terminal, so the foreground window IS that
        // terminal. Claim/refresh the binding every time. Best effort — a
        // failed claim must never break hook processing.
        try
        {
            var claimed = await windowBinding.ClaimForegroundAsync(instance, WindowBindingSource.PromptSubmit, ct);

            // Bindings are mutable per-prompt: a successful claim may have
            // moved the instance to a different window, so the desktop id
            // captured at session start can be stale. Refresh it.
            if (claimed)
                RefreshDesktopId(instance);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Foreground window claim failed for instance {Id} on prompt submit", instance.Id);
        }

        var promptPreview = payload.Prompt is { Length: > 60 }
            ? payload.Prompt[..60] + "..."
            : payload.Prompt ?? "(empty)";

        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, instance.Id,
            Message: $"Prompt: {promptPreview}"));
    }

    public Task HandleSessionEndAsync(HookPayload payload, CancellationToken ct = default)
    {
        if (payload.SessionId is null) return Task.CompletedTask;

        var instance = registry.GetBySessionId(payload.SessionId);
        if (instance is null)
        {
            logger.LogDebug("SessionEnd for unknown session {SessionId}", payload.SessionId);
            return Task.CompletedTask;
        }

        var id = instance.Id;
        registry.Unregister(payload.SessionId);

        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, id,
            Message: "Session ended"));

        logger.LogInformation("Instance {Id} deregistered (session {SessionId} ended)", id, payload.SessionId);
        return Task.CompletedTask;
    }

    private async Task InjectPersonalityAsync(InstanceInfo instance, CancellationToken ct)
    {
        var personality = personalityManager.GetForSlot(instance.Id);
        if (personality?.Personality is null)
        {
            logger.LogDebug("No personality configured for slot {Slot}", instance.Id);
            return;
        }

        if (instance.WindowHandle == nint.Zero)
        {
            logger.LogWarning("Cannot inject personality for slot {Slot}: no window handle", instance.Id);
            return;
        }

        // Build the path to the slot config file and convert to WSL format
        // so the skill's !`jq` preprocessing can read it from the WSL shell.
        var configPath = personalityManager.ResolveSlotConfigPath(instance.Id);
        if (configPath is null)
        {
            logger.LogWarning("No config file path for slot {Slot}", instance.Id);
            return;
        }

        var wslPath = ToWslPath(configPath);
        var skillCommand = $"/personality-override {wslPath}";

        // Delay to let the session initialize before injecting
        await Task.Delay(2000, ct);

        try
        {
            await keystrokeInjector.InjectTextAndSubmitAsync(instance.WindowHandle, skillCommand, ct);
            logger.LogInformation("Injected personality skill '{Name}' into slot {Slot} (path: {Path})",
                personality.Name, instance.Id, wslPath);

            eventBus.Publish(new InstanceEvent(
                InstanceEventType.ActivityLogged, instance.Id,
                Message: $"Personality loaded: {personality.Name}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to inject personality for slot {Slot}", instance.Id);
        }
    }

    /// <summary>
    /// Converts a Windows path to a WSL-compatible path.
    /// E.g., C:\Users\krogh\AppData\... → /mnt/c/Users/krogh/AppData/...
    /// </summary>
    private static string ToWslPath(string windowsPath)
    {
        if (windowsPath.Length >= 2 && windowsPath[1] == ':')
        {
            var driveLetter = char.ToLowerInvariant(windowsPath[0]);
            var rest = windowsPath[2..].Replace('\\', '/');
            return $"/mnt/{driveLetter}{rest}";
        }

        return windowsPath.Replace('\\', '/');
    }

    private void RefreshDesktopId(InstanceInfo instance)
    {
        if (instance.WindowHandle == nint.Zero || !virtualDesktopService.IsAvailable)
            return;

        var desktopId = virtualDesktopService.GetWindowDesktopId(instance.WindowHandle);
        instance.DesktopId = desktopId == Guid.Empty ? null : desktopId;
    }

    private async Task WarmupNotificationCacheAsync(string slotId, CancellationToken ct)
    {
        try
        {
            await notificationCacheWarmer.WarmupAsync(slotId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notification cache warmup failed for slot {Slot}", slotId);
        }
    }
}
