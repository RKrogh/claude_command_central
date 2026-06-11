using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using CommandCentral.Daemon;
using CommandCentral.Input;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommandCentral.Integration.Tests;

public class OrchestratorTests
{
    private readonly InMemoryEventBus _eventBus = new();
    private readonly InMemoryInstanceRegistry _registry;
    private readonly FakeWindowManager _windowManager = new();
    private readonly Orchestrator _orchestrator;

    public OrchestratorTests()
    {
        _registry = new InMemoryInstanceRegistry(_eventBus);
        var windowBinding = new WindowBindingService(
            _windowManager, _registry, NullLogger<WindowBindingService>.Instance)
        {
            MarkerPropagationDelay = TimeSpan.Zero
        };
        _orchestrator = new Orchestrator(
            _registry,
            _eventBus,
            windowBinding,
            new NullTtsNotifier(),
            new NullPersonalityManager(),
            new NullKeystrokeInjector(),
            new NullNotificationCacheWarmer(),
            NullLogger<Orchestrator>.Instance);
    }

    private sealed class NullTtsNotifier : ITtsNotifier
    {
        public Task NotifyInstanceReadyAsync(string instanceId, string? voiceProfile = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyDoneAsync(string instanceId, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReadResponseAsync(string text, string? voiceProfile = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullPersonalityManager : IPersonalityManager
    {
        public PersonalityConfig? GetForSlot(string slotId) => null;
        public string? ResolveVoiceRefPath(string slotId) => null;
        public string? ResolveSlotConfigPath(string slotId) => null;
        public void Reload() { }
    }

    private sealed class NullKeystrokeInjector : IKeystrokeInjector
    {
        public Task InjectTextAsync(nint windowHandle, string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task InjectTextAndSubmitAsync(nint windowHandle, string text, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullNotificationCacheWarmer : INotificationCacheWarmer
    {
        public Task WarmupAsync(string slotId, CancellationToken ct = default) => Task.CompletedTask;
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
    public async Task HandleSessionStart_BindsForegroundWindow_AndStoresWtSession()
    {
        _windowManager.ForegroundWindow = 0x42;

        await _orchestrator.HandleSessionStartAsync(
            new HookPayload { SessionId = "abc-123" }, windowMarker: null, wtSession: "wt-guid-1");

        var instance = _registry.GetBySessionId("abc-123");
        Assert.Equal(0x42, instance!.WindowHandle);
        Assert.Equal(WindowBindingSource.SessionStartForeground, instance.WindowBindingSource);
        Assert.Equal("wt-guid-1", instance.WtSession);
    }

    [Fact]
    public async Task HandlePromptSubmit_ClaimsForegroundWindow()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        // Instance 2+ scenario: no window resolved at session start,
        // but the user is sitting in the terminal when submitting a prompt.
        _windowManager.ForegroundWindow = 0x77;
        await _orchestrator.HandlePromptSubmitAsync(new HookPayload
        {
            SessionId = "abc-123",
            Prompt = "do work"
        });

        var instance = _registry.GetBySessionId("abc-123");
        Assert.Equal(0x77, instance!.WindowHandle);
        Assert.Equal(WindowBindingSource.PromptSubmit, instance.WindowBindingSource);
    }

    [Fact]
    public async Task HandlePromptSubmit_RebindsWhenForegroundChanged()
    {
        _windowManager.ForegroundWindow = 0x10;
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        // The terminal window changed (e.g. session moved to a new terminal).
        _windowManager.ForegroundWindow = 0x20;
        await _orchestrator.HandlePromptSubmitAsync(new HookPayload { SessionId = "abc-123", Prompt = "x" });

        Assert.Equal(0x20, _registry.GetBySessionId("abc-123")!.WindowHandle);
    }

    [Fact]
    public async Task HandlePromptSubmit_KeepsBindingWhenNoForeground()
    {
        _windowManager.ForegroundWindow = 0x10;
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        _windowManager.ForegroundWindow = nint.Zero;
        await _orchestrator.HandlePromptSubmitAsync(new HookPayload { SessionId = "abc-123", Prompt = "x" });

        Assert.Equal(0x10, _registry.GetBySessionId("abc-123")!.WindowHandle);
    }

    [Fact]
    public async Task HandlePromptSubmit_StoresWtSession()
    {
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "abc-123" });

        await _orchestrator.HandlePromptSubmitAsync(
            new HookPayload { SessionId = "abc-123", Prompt = "x" }, wtSession: "wt-guid-2");

        Assert.Equal("wt-guid-2", _registry.GetBySessionId("abc-123")!.WtSession);
    }

    [Fact]
    public async Task SecondInstanceOnSameDesktop_GetsWindowOnPromptSubmit()
    {
        // Instance 1 starts in terminal A (foreground).
        _windowManager.ForegroundWindow = 0xA;
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "s1" });

        // Instance 2 starts in terminal B on the same desktop.
        _windowManager.ForegroundWindow = 0xB;
        await _orchestrator.HandleSessionStartAsync(new HookPayload { SessionId = "s2" });
        Assert.Equal(0xB, _registry.GetBySessionId("s2")!.WindowHandle);

        // User goes back to instance 1's terminal and submits a prompt — binding stays correct.
        _windowManager.ForegroundWindow = 0xA;
        await _orchestrator.HandlePromptSubmitAsync(new HookPayload { SessionId = "s1", Prompt = "x" });
        Assert.Equal(0xA, _registry.GetBySessionId("s1")!.WindowHandle);
        Assert.Equal(0xB, _registry.GetBySessionId("s2")!.WindowHandle);
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
