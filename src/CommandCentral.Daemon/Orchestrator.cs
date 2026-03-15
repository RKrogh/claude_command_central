using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Daemon;

public sealed class Orchestrator(
    IInstanceRegistry registry,
    IEventBus eventBus,
    ILogger<Orchestrator> logger) : IOrchestrator
{
    public Task HandleSessionStartAsync(HookPayload payload, CancellationToken ct = default)
    {
        if (payload.SessionId is null)
        {
            logger.LogWarning("SessionStart hook received without session_id");
            return Task.CompletedTask;
        }

        var existing = registry.GetBySessionId(payload.SessionId);
        if (existing is not null)
        {
            logger.LogInformation("Session {SessionId} already registered as instance {Id}", payload.SessionId, existing.Id);
            return Task.CompletedTask;
        }

        var instance = registry.Register(payload.SessionId, payload.Cwd);
        logger.LogInformation("Registered instance {Id} for session {SessionId} (project: {Project})",
            instance.Id, payload.SessionId, instance.ProjectName ?? "unknown");

        return Task.CompletedTask;
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
}
