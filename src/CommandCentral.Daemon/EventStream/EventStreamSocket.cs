using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using CommandCentral.Core.Api;
using CommandCentral.Core.Services;

namespace CommandCentral.Daemon.EventStream;

/// <summary>
/// Pumps event-bus traffic to a single connected WebSocket client.
/// Sends a full state snapshot on connect, then relays instance and daemon
/// events as JSON text messages until either side closes.
/// </summary>
public sealed class EventStreamSocket(
    IInstanceRegistry registry,
    InstanceActivityLog activityLog,
    IEventBus eventBus,
    ILogger logger,
    int channelCapacity = 256)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(WebSocket socket, CancellationToken ct)
    {
        // Bounded queue so a stalled client can't grow memory without limit.
        // Dropping is not free, though: losing a Removed or
        // SelectedInstanceChanged event would leave the client permanently
        // stale, so any drop schedules a fresh snapshot that supersedes
        // whatever was lost.
        var resyncNeeded = 0;
        var channel = Channel.CreateBounded<EventStreamMessage>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            },
            _ => Interlocked.Exchange(ref resyncNeeded, 1));

        using var instanceSubscription = eventBus.SubscribeInstances(instanceEvent =>
            channel.Writer.TryWrite(new EventStreamMessage
            {
                Kind = EventStreamMessageKind.Instance,
                Instance = ApiMapper.ToDto(instanceEvent, registry, activityLog)
            }));

        using var daemonSubscription = eventBus.SubscribeDaemon(daemonEvent =>
            channel.Writer.TryWrite(new EventStreamMessage
            {
                Kind = EventStreamMessageKind.Daemon,
                Daemon = ApiMapper.ToDto(daemonEvent)
            }));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // We never expect client messages, but reading is how we observe the
        // client closing the connection.
        var readTask = ReadUntilClosedAsync(socket, cts);

        try
        {
            await SendAsync(socket, new EventStreamMessage
            {
                Kind = EventStreamMessageKind.Snapshot,
                Snapshot = ApiMapper.BuildSnapshot(registry, activityLog)
            }, cts.Token);

            while (await channel.Reader.WaitToReadAsync(cts.Token))
            {
                while (channel.Reader.TryRead(out var message))
                    await SendAsync(socket, message, cts.Token);

                // Events were dropped while the queue was full — the client
                // may have missed something it can't recover from, so send a
                // fresh snapshot built after the drained backlog.
                if (Interlocked.Exchange(ref resyncNeeded, 0) == 1)
                {
                    logger.LogDebug("Event stream queue overflowed; resyncing client with a snapshot");
                    await SendAsync(socket, new EventStreamMessage
                    {
                        Kind = EventStreamMessageKind.Snapshot,
                        Snapshot = ApiMapper.BuildSnapshot(registry, activityLog)
                    }, cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client closed or daemon shutting down — normal exit.
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Event stream client disconnected abruptly");
        }
        finally
        {
            await cts.CancelAsync();
            await readTask;
            await TryCloseAsync(socket);
        }
    }

    private static async Task ReadUntilClosedAsync(WebSocket socket, CancellationTokenSource cts)
    {
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            // Stop the send loop once the client is gone.
            await cts.CancelAsync();
        }
    }

    private static async Task SendAsync(WebSocket socket, EventStreamMessage message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private async Task TryCloseAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;

        try
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "daemon closing", closeTimeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            logger.LogDebug(ex, "Graceful WebSocket close failed");
        }
    }
}
