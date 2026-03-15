using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using CommandCentral.Daemon;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommandCentral.Integration.Tests;

public class OrchestratorTests
{
    private readonly InMemoryEventBus _eventBus = new();
    private readonly InMemoryInstanceRegistry _registry;
    private readonly Orchestrator _orchestrator;

    public OrchestratorTests()
    {
        _registry = new InMemoryInstanceRegistry(_eventBus);
        _orchestrator = new Orchestrator(_registry, _eventBus, NullLogger<Orchestrator>.Instance);
    }

    [Fact]
    public async Task HandleSessionStart_RegistersNewInstance()
    {
        var payload = new HookPayload { SessionId = "abc-123", Cwd = "/home/user/project" };

        await _orchestrator.HandleSessionStartAsync(payload);

        var instance = _registry.GetBySessionId("abc-123");
        Assert.NotNull(instance);
        Assert.Equal("1", instance.Id);
        Assert.Equal("project", instance.ProjectName);
    }

    [Fact]
    public async Task HandleSessionStart_IgnoresDuplicateSession()
    {
        var payload = new HookPayload { SessionId = "abc-123" };

        await _orchestrator.HandleSessionStartAsync(payload);
        await _orchestrator.HandleSessionStartAsync(payload);

        Assert.Single(_registry.GetAll());
    }

    [Fact]
    public async Task HandleSessionStart_IgnoresNullSessionId()
    {
        var payload = new HookPayload { SessionId = null };

        await _orchestrator.HandleSessionStartAsync(payload);

        Assert.Empty(_registry.GetAll());
    }

    [Fact]
    public async Task HandleStop_SetsStateToWaitingForInput()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        await _orchestrator.HandleStopAsync(new HookPayload
        {
            SessionId = "abc-123",
            LastAssistantMessage = "Done!"
        });

        var instance = _registry.GetBySessionId("abc-123");
        Assert.Equal(InstanceState.WaitingForInput, instance!.State);
        Assert.Equal("Done!", instance.LastAssistantMessage);
    }

    [Fact]
    public async Task HandleStop_PublishesActivityEvent()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        var events = new List<InstanceEvent>();
        _eventBus.SubscribeInstances(e => events.Add(e));

        await _orchestrator.HandleStopAsync(new HookPayload { SessionId = "abc-123" });

        Assert.Contains(events, e =>
            e.Type == InstanceEventType.ActivityLogged &&
            e.Message == "Response complete");
    }

    [Fact]
    public async Task HandlePromptSubmit_SetsStateToBusy()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        await _orchestrator.HandlePromptSubmitAsync(new HookPayload
        {
            SessionId = "abc-123",
            Prompt = "fix the auth bug"
        });

        var instance = _registry.GetBySessionId("abc-123");
        Assert.Equal(InstanceState.Busy, instance!.State);
    }

    [Fact]
    public async Task HandlePromptSubmit_TruncatesLongPrompts()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        var events = new List<InstanceEvent>();
        _eventBus.SubscribeInstances(e => events.Add(e));

        var longPrompt = new string('x', 100);
        await _orchestrator.HandlePromptSubmitAsync(new HookPayload
        {
            SessionId = "abc-123",
            Prompt = longPrompt
        });

        var activityEvent = events.First(e => e.Type == InstanceEventType.ActivityLogged);
        Assert.EndsWith("...", activityEvent.Message!);
    }

    [Fact]
    public async Task HandleNotification_SetsStateToIdle()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });
        _registry.UpdateState("abc-123", InstanceState.Busy);

        await _orchestrator.HandleNotificationAsync(new HookPayload { SessionId = "abc-123" });

        var instance = _registry.GetBySessionId("abc-123");
        Assert.Equal(InstanceState.Idle, instance!.State);
    }

    [Fact]
    public async Task HandleStop_IgnoresUnknownSession()
    {
        var exception = await Record.ExceptionAsync(() =>
            _orchestrator.HandleStopAsync(new HookPayload { SessionId = "unknown" }));

        Assert.Null(exception);
    }

    [Fact]
    public async Task HandleSessionEnd_UnregistersInstance()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123", Cwd = "/proj" });
        Assert.Single(_registry.GetAll());

        await _orchestrator.HandleSessionEndAsync(new HookPayload { SessionId = "abc-123" });

        Assert.Empty(_registry.GetAll());
        Assert.Null(_registry.GetBySessionId("abc-123"));
    }

    [Fact]
    public async Task HandleSessionEnd_PublishesEvent()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        var events = new List<InstanceEvent>();
        _eventBus.SubscribeInstances(e => events.Add(e));

        await _orchestrator.HandleSessionEndAsync(new HookPayload { SessionId = "abc-123" });

        Assert.Contains(events, e =>
            e.Type == InstanceEventType.ActivityLogged &&
            e.Message == "Session ended");
    }

    [Fact]
    public async Task HandleSessionEnd_IgnoresUnknownSession()
    {
        var exception = await Record.ExceptionAsync(() =>
            _orchestrator.HandleSessionEndAsync(new HookPayload { SessionId = "unknown" }));

        Assert.Null(exception);
    }

    [Fact]
    public async Task HandleSessionEnd_IgnoresNullSessionId()
    {
        var exception = await Record.ExceptionAsync(() =>
            _orchestrator.HandleSessionEndAsync(new HookPayload { SessionId = null }));

        Assert.Null(exception);
    }

    [Fact]
    public async Task FullLifecycle_SessionStartPromptStopNotification()
    {
        // Start
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "s1", Cwd = "/proj" });
        Assert.Equal(InstanceState.Idle, _registry.GetBySessionId("s1")!.State);

        // Prompt submitted
        await _orchestrator.HandlePromptSubmitAsync(new HookPayload { SessionId = "s1", Prompt = "do something" });
        Assert.Equal(InstanceState.Busy, _registry.GetBySessionId("s1")!.State);

        // Response complete
        await _orchestrator.HandleStopAsync(new HookPayload { SessionId = "s1", LastAssistantMessage = "Done" });
        Assert.Equal(InstanceState.WaitingForInput, _registry.GetBySessionId("s1")!.State);

        // Goes idle
        await _orchestrator.HandleNotificationAsync(new HookPayload { SessionId = "s1" });
        Assert.Equal(InstanceState.Idle, _registry.GetBySessionId("s1")!.State);

        // Session ends
        await _orchestrator.HandleSessionEndAsync(new HookPayload { SessionId = "s1" });
        Assert.Null(_registry.GetBySessionId("s1"));
        Assert.Empty(_registry.GetAll());
    }
}
