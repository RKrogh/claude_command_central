using CommandCentral.Core.Events;
using CommandCentral.Core.Services;
using CommandCentral.Input;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommandCentral.Integration.Tests;

public class ResponseReadHandlerTests
{
    private readonly InMemoryEventBus _eventBus = new();
    private readonly InMemoryInstanceRegistry _registry;
    private readonly FakeTtsNotifier _tts = new();
    private readonly ResponseReadHandler _handler;
    private readonly List<DaemonEvent> _daemonEvents = [];

    public ResponseReadHandlerTests()
    {
        _registry = new InMemoryInstanceRegistry(_eventBus);
        _handler = new ResponseReadHandler(
            _registry, _tts, _eventBus, NullLogger<ResponseReadHandler>.Instance);
        _eventBus.SubscribeDaemon(e => { lock (_daemonEvents) _daemonEvents.Add(e); });
    }

    [Fact]
    public async Task ToggleRead_NoInstanceSelected_DoesNothing()
    {
        await _handler.ToggleReadAsync();

        Assert.Empty(_tts.Reads);
        Assert.Empty(_daemonEvents);
        Assert.False(_handler.IsReading);
    }

    [Fact]
    public async Task ToggleRead_NoLastMessage_DoesNotRead()
    {
        var instance = _registry.Register("session-1");

        await _handler.ToggleReadAsync(instance.Id);

        Assert.Empty(_tts.Reads);
        Assert.False(_handler.IsReading);
    }

    [Fact]
    public async Task ToggleRead_ReadsSelectedInstanceResponse()
    {
        var instance = _registry.Register("session-1");
        instance.LastAssistantMessage = "The answer is 42.";
        _registry.SelectedInstanceId = instance.Id;

        await _handler.ToggleReadAsync();

        var read = Assert.Single(_tts.Reads);
        Assert.Equal("The answer is 42.", read.Text);
        Assert.Equal(instance.Id, read.Profile);
        Assert.Contains(_daemonEvents, e => e.Type == DaemonEventType.TtsStarted && e.InstanceId == instance.Id);
        Assert.Contains(_daemonEvents, e => e.Type == DaemonEventType.TtsStopped && e.InstanceId == instance.Id);
        Assert.False(_handler.IsReading);
    }

    [Fact]
    public async Task ToggleRead_SecondPress_StopsReading()
    {
        var instance = _registry.Register("session-1");
        instance.LastAssistantMessage = "A long response.";
        _tts.Block = true;

        var first = _handler.ToggleReadAsync(instance.Id);
        await _tts.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_handler.IsReading);

        await _handler.ToggleReadAsync(instance.Id);
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(_handler.IsReading);
        Assert.Single(_tts.Reads);
        Assert.Contains(_daemonEvents, e => e.Type == DaemonEventType.TtsStopped);
    }

    [Fact]
    public async Task ToggleRead_DifferentInstance_SwitchesReading()
    {
        var first = _registry.Register("session-1");
        first.LastAssistantMessage = "First response.";
        var second = _registry.Register("session-2");
        second.LastAssistantMessage = "Second response.";
        _tts.Block = true;

        var firstRead = _handler.ToggleReadAsync(first.Id);
        await _tts.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _tts.ReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRead = _handler.ToggleReadAsync(second.Id);
        await _tts.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await firstRead.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(_handler.IsReading);
        Assert.Equal(2, _tts.Reads.Count);
        Assert.Equal(second.Id, _tts.Reads[1].Profile);

        // Stop the second read so the test ends clean
        await _handler.ToggleReadAsync(second.Id);
        await secondRead.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(_handler.IsReading);
    }

    private sealed class FakeTtsNotifier : ITtsNotifier
    {
        public readonly List<(string Text, string? Profile)> Reads = [];
        public bool Block;
        public TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task NotifyInstanceReadyAsync(string instanceId, string? voiceProfile = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyDoneAsync(string instanceId, CancellationToken ct = default)
            => Task.CompletedTask;

        public async Task ReadResponseAsync(string text, string? voiceProfile = null, CancellationToken ct = default)
        {
            lock (Reads) Reads.Add((text, voiceProfile));
            ReadStarted.TrySetResult();

            if (!Block)
                return;

            // Mirror TtsNotifier: a cancelled read returns normally
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
        }
    }
}
