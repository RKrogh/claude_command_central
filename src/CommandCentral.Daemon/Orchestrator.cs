using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Daemon;

public sealed class Orchestrator(
    IInstanceRegistry registry,
    IEventBus eventBus,
    IWindowManager windowManager,
    ILogger<Orchestrator> logger) : IOrchestrator
{
    public async Task HandleSessionStartAsync(HookPayload payload, string? windowMarker = null, CancellationToken ct = default)
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

        // Resolve the terminal window for this session.
        var windowHandle = await ResolveWindowHandleAsync(instance, windowMarker, ct);
        instance.WindowHandle = windowHandle;

        logger.LogInformation("Registered instance {Id} for session {SessionId} (project: {Project}, window: 0x{Handle:X}, marker: {Marker})",
            instance.Id, payload.SessionId, instance.ProjectName ?? "unknown", windowHandle, windowMarker ?? "none");
    }

    public Task HandleStopAsync(HookPayload payload, CancellationToken ct = default)
    {
        if (payload.SessionId is null) return Task.CompletedTask;

        var instance = registry.GetBySessionId(payload.SessionId);
        if (instance is null)
        {
            logger.LogDebug("Stop hook for unknown session {SessionId}", payload.SessionId);
            return Task.CompletedTask;
        }

        instance.LastAssistantMessage = payload.LastAssistantMessage;
        registry.UpdateState(payload.SessionId, InstanceState.WaitingForInput);

        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, instance.Id,
            Message: "Response complete"));

        logger.LogInformation("Instance {Id} response complete", instance.Id);

        // TODO: trigger TTS notification
        return Task.CompletedTask;
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

    public Task HandlePromptSubmitAsync(HookPayload payload, CancellationToken ct = default)
    {
        if (payload.SessionId is null) return Task.CompletedTask;

        var instance = registry.GetBySessionId(payload.SessionId);
        if (instance is null) return Task.CompletedTask;

        registry.UpdateState(payload.SessionId, InstanceState.Busy);

        var promptPreview = payload.Prompt is { Length: > 60 }
            ? payload.Prompt[..60] + "..."
            : payload.Prompt ?? "(empty)";

        eventBus.Publish(new InstanceEvent(
            InstanceEventType.ActivityLogged, instance.Id,
            Message: $"Prompt: {promptPreview}"));

        return Task.CompletedTask;
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

    private async Task<nint> ResolveWindowHandleAsync(
        InstanceInfo instance, string? windowMarker, CancellationToken ct)
    {
        if (windowMarker is not null)
        {
            // The hook set the terminal title to "cc:<marker>" — find that window.
            // Delay to let the terminal title change propagate to the window manager.
            await Task.Delay(500, ct);

            var markerTag = $"cc:{windowMarker}";
            var handle = await windowManager.FindWindowByTitleAsync(markerTag, ct);
            if (handle != nint.Zero)
            {
                logger.LogDebug("Matched window by marker '{Marker}'", markerTag);
                return handle;
            }

            logger.LogWarning("Window marker '{Marker}' not found in any window title", markerTag);
        }

        // Fallback: use foreground window
        var claimedHandles = registry.GetAll()
            .Where(i => i.Id != instance.Id && i.WindowHandle != nint.Zero)
            .Select(i => i.WindowHandle)
            .ToHashSet();

        var foreground = await windowManager.GetForegroundWindowAsync(ct);
        if (foreground != nint.Zero && !claimedHandles.Contains(foreground))
        {
            logger.LogDebug("Using foreground window 0x{Handle:X} as fallback", foreground);
            return foreground;
        }

        logger.LogWarning("Could not resolve window handle for instance {Id}", instance.Id);
        return nint.Zero;
    }
}
