using System.Net;
using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CommandCentral.Core.Api;
using CommandCentral.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CommandCentral.Integration.Tests;

public class EventsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public EventsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("COMMANDCENTRAL_HEADLESS_ONLY", "true");
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task NonWebSocketRequest_Returns400()
    {
        var response = await _client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Connect_ReceivesSnapshotFirst()
    {
        await RegisterInstanceAsync("ws-snap-1", "/project/snap");

        using var socket = await ConnectAsync();
        var message = await ReceiveMessageAsync(socket);

        Assert.Equal(EventStreamMessageKind.Snapshot, message.Kind);
        Assert.NotNull(message.Snapshot);
        Assert.Contains(message.Snapshot.Instances, i => i.SessionId == "ws-snap-1" && i.ProjectName == "snap");
    }

    [Fact]
    public async Task SessionStart_StreamsAddedEvent()
    {
        using var socket = await ConnectAsync();
        await ReceiveMessageAsync(socket); // snapshot

        await RegisterInstanceAsync("ws-add-1", "/project/added");

        var added = await ReceiveUntilAsync(socket, m =>
            m.Kind == EventStreamMessageKind.Instance &&
            m.Instance?.Type == "Added" &&
            m.Instance.Instance?.SessionId == "ws-add-1");

        Assert.NotNull(added.Instance?.Instance);
        Assert.Equal("added", added.Instance.Instance.ProjectName);
        Assert.Equal("Idle", added.Instance.Instance.State);
    }

    [Fact]
    public async Task StopHook_StreamsStateChangedEvent()
    {
        await RegisterInstanceAsync("ws-stop-1", "/project/stopper");

        using var socket = await ConnectAsync();
        await ReceiveMessageAsync(socket); // snapshot

        await _client.PostAsJsonAsync("/hooks/stop",
            new HookPayload { SessionId = "ws-stop-1", LastAssistantMessage = "done" });

        var stateChanged = await ReceiveUntilAsync(socket, m =>
            m.Kind == EventStreamMessageKind.Instance &&
            m.Instance?.Type == "StateChanged" &&
            m.Instance.Instance?.SessionId == "ws-stop-1");

        Assert.Equal("WaitingForInput", stateChanged.Instance!.State);
    }

    [Fact]
    public async Task SessionEnd_StreamsRemovedEvent()
    {
        await RegisterInstanceAsync("ws-end-1", "/project/ender");

        using var socket = await ConnectAsync();
        await ReceiveMessageAsync(socket); // snapshot

        await _client.PostAsJsonAsync("/hooks/session-end", new HookPayload { SessionId = "ws-end-1" });

        var removed = await ReceiveUntilAsync(socket, m =>
            m.Kind == EventStreamMessageKind.Instance &&
            m.Instance?.Type == "Removed");

        // Instance is already gone from the registry, so no snapshot rides along.
        Assert.Null(removed.Instance!.Instance);
    }

    [Fact]
    public async Task Snapshot_IncludesRecentActivity()
    {
        await RegisterInstanceAsync("ws-act-1", "/project/active");
        await _client.PostAsJsonAsync("/hooks/prompt-submit",
            new HookPayload { SessionId = "ws-act-1", Prompt = "do the thing" });

        using var socket = await ConnectAsync();
        var message = await ReceiveMessageAsync(socket);

        var instance = message.Snapshot!.Instances.First(i => i.SessionId == "ws-act-1");
        Assert.Contains(instance.RecentActivity, e => e.Message.Contains("do the thing"));
    }

    [Fact]
    public async Task FragmentedMultiByteMessage_DecodesIntact()
    {
        // Activity full of two-byte UTF-8 chars, received with a tiny buffer
        // so the message arrives in many partial fragments whose boundaries
        // routinely fall inside a multi-byte sequence. Per-fragment decoding
        // would corrupt the JSON; accumulate-then-decode must survive it.
        // 60 chars: the prompt preview truncation limit, so the full prompt
        // text lands in the activity log.
        var prompt = string.Concat(Enumerable.Repeat("åäö", 20));
        await RegisterInstanceAsync("ws-utf8-1", "/project/blåbär");
        await _client.PostAsJsonAsync("/hooks/prompt-submit",
            new HookPayload { SessionId = "ws-utf8-1", Prompt = prompt });

        using var socket = await ConnectAsync();
        var message = await ReceiveMessageAsync(socket, bufferSize: 7);

        Assert.Equal(EventStreamMessageKind.Snapshot, message.Kind);
        var instance = message.Snapshot!.Instances.First(i => i.SessionId == "ws-utf8-1");
        Assert.Contains(instance.RecentActivity, e => e.Message.Contains(prompt));
    }

    [Fact]
    public async Task State_IncludesWindowBindingAndActivity()
    {
        await RegisterInstanceAsync("ws-state-1", "/project/statey");

        var snapshot = await _client.GetFromJsonAsync<StateSnapshotDto>("/api/state", JsonOptions);

        var instance = snapshot!.Instances.First(i => i.SessionId == "ws-state-1");
        Assert.False(instance.WindowBound); // headless mode never resolves a window
        Assert.Null(instance.DesktopId);
        Assert.NotEmpty(instance.RecentActivity);
    }

    private async Task RegisterInstanceAsync(string sessionId, string cwd)
    {
        var response = await _client.PostAsJsonAsync("/hooks/session-start",
            new HookPayload { SessionId = sessionId, Cwd = cwd });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<WebSocket> ConnectAsync()
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        return await wsClient.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/api/events"),
            CancellationToken.None);
    }

    private static async Task<EventStreamMessage> ReceiveMessageAsync(WebSocket socket, int bufferSize = 64 * 1024)
    {
        using var cts = new CancellationTokenSource(ReceiveTimeout);
        var buffer = new byte[bufferSize];
        using var accumulator = new MemoryStream();

        // Accumulate raw bytes and decode once at end-of-message; decoding
        // per fragment would corrupt multi-byte UTF-8 chars split across a
        // fragment boundary.
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            accumulator.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
                break;
        }

        var json = Encoding.UTF8.GetString(accumulator.GetBuffer(), 0, (int)accumulator.Length);
        var message = JsonSerializer.Deserialize<EventStreamMessage>(json, JsonOptions);
        Assert.NotNull(message);
        return message;
    }

    private static async Task<EventStreamMessage> ReceiveUntilAsync(
        WebSocket socket, Func<EventStreamMessage, bool> predicate)
    {
        // Other tests on the shared server also produce events; skip the noise.
        for (var i = 0; i < 50; i++)
        {
            var message = await ReceiveMessageAsync(socket);
            if (predicate(message))
                return message;
        }

        throw new TimeoutException("Expected event was not received within 50 messages.");
    }
}
