using System.Net.WebSockets;
using System.Text.Json;
using CommandCentral.Core.Api;
using CommandCentral.Core.Events;
using CommandCentral.Core.Services;
using CommandCentral.Daemon.EventStream;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommandCentral.Integration.Tests;

public class EventStreamSocketTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task QueueOverflow_ResyncsClientWithFreshSnapshot()
    {
        var bus = new InMemoryEventBus();
        var registry = new InMemoryInstanceRegistry(bus);
        using var activityLog = new InstanceActivityLog(bus);
        var stream = new EventStreamSocket(
            registry, activityLog, bus, NullLogger.Instance, channelCapacity: 1);

        using var socket = new GatedWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runTask = stream.RunAsync(socket, cts.Token);

        // Wait for the on-connect snapshot, sent while the gate is open.
        await WaitUntilAsync(() => socket.Sent.Count >= 1);
        Assert.Equal(EventStreamMessageKind.Snapshot, socket.Sent[0].Kind);

        // The gate now blocks further sends. With capacity 1 the pump can
        // absorb at most two events (one in-flight, one queued); the rest
        // are dropped, which must trigger a resync.
        for (var i = 0; i < 5; i++)
            bus.Publish(new InstanceEvent(InstanceEventType.Added, $"{i}"));

        socket.OpenGate();

        await WaitUntilAsync(() =>
            socket.Sent.Skip(1).Any(m => m.Kind == EventStreamMessageKind.Snapshot));

        cts.Cancel();
        await runTask;

        var sent = socket.Sent;
        Assert.True(sent.Count(m => m.Kind == EventStreamMessageKind.Instance) < 5,
            "expected the overflowing queue to drop events");
        var lastSnapshot = sent.FindLastIndex(m => m.Kind == EventStreamMessageKind.Snapshot);
        var lastInstance = sent.FindLastIndex(m => m.Kind == EventStreamMessageKind.Instance);
        Assert.True(lastSnapshot > lastInstance,
            "expected the resync snapshot to follow the drained backlog");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(condition(), "condition not met within timeout");
    }

    /// <summary>
    /// WebSocket fake whose sends block on a gate after the first message,
    /// simulating a stalled client so the bounded channel overflows.
    /// </summary>
    private sealed class GatedWebSocket : WebSocket
    {
        private readonly SemaphoreSlim _gate = new(1);
        private readonly Lock _lock = new();
        private readonly List<EventStreamMessage> _sent = [];
        private WebSocketState _state = WebSocketState.Open;

        public List<EventStreamMessage> Sent
        {
            get
            {
                lock (_lock)
                {
                    return [.. _sent];
                }
            }
        }

        public void OpenGate() => _gate.Release(100);

        public override async Task SendAsync(
            ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);

            var message = JsonSerializer.Deserialize<EventStreamMessage>(
                buffer.AsSpan(), JsonOptions)!;
            lock (_lock)
            {
                _sent.Add(message);
            }
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            // The daemon never expects client messages; block until cancelled.
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override void Dispose()
        {
        }
    }
}
