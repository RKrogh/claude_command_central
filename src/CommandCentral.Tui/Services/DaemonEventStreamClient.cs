using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CommandCentral.Core.Api;

namespace CommandCentral.Tui.Services;

/// <summary>
/// Maintains a WebSocket connection to the daemon's /api/events stream and
/// feeds every message into the <see cref="TuiStateStore"/>. Reconnects with
/// exponential backoff when the daemon goes away, so a daemon restart only
/// shows up as a brief "disconnected" status.
/// </summary>
public sealed class DaemonEventStreamClient(string baseUrl, TuiStateStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Uri _eventsUri = BuildWebSocketUri(baseUrl);
    private readonly ReconnectBackoff _backoff = new();

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(_eventsUri, ct);

                _backoff.Reset();
                store.SetConnected(true);

                await ReceiveLoopAsync(socket, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or JsonException)
            {
                // Daemon unreachable, connection dropped, or garbled frame —
                // fall through to the backoff and try again.
            }

            store.SetConnected(false);

            try
            {
                await Task.Delay(_backoff.NextDelay(), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var json = await ReceiveTextMessageAsync(socket, buffer, ct);
            if (json is null)
                return;

            var message = JsonSerializer.Deserialize<EventStreamMessage>(json, JsonOptions);
            if (message is not null)
                store.Apply(message);
        }
    }

    /// <summary>
    /// Reads one complete text message from the socket. Raw bytes are
    /// accumulated across fragments and decoded once at end-of-message, so a
    /// multi-byte UTF-8 character split across a fragment boundary survives
    /// intact. Returns <c>null</c> when the peer closes the connection.
    /// </summary>
    internal static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var accumulator = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            accumulator.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(accumulator.GetBuffer(), 0, (int)accumulator.Length);
        }
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        var uri = new UriBuilder(new Uri(new Uri(baseUrl), "/api/events"));
        uri.Scheme = uri.Scheme == "https" ? "wss" : "ws";
        return uri.Uri;
    }
}
