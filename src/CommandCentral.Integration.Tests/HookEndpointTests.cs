using System.Net;
using System.Net.Http.Json;
using CommandCentral.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CommandCentral.Integration.Tests;

public class HookEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HookEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("COMMANDCENTRAL_HEADLESS_ONLY", "true");
        }).CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SessionStart_RegistersInstance()
    {
        var payload = new HookPayload { SessionId = "test-1", Cwd = "/project/alpha" };

        var response = await _client.PostAsJsonAsync("/hooks/session-start", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await _client.GetFromJsonAsync<StateResponse>("/api/state");
        Assert.NotNull(state);
        Assert.Contains(state.Instances, i => i.SessionId == "test-1" && i.ProjectName == "alpha");
    }

    [Fact]
    public async Task Stop_UpdatesStateToWaitingForInput()
    {
        var payload = new HookPayload { SessionId = "test-stop-1", Cwd = "/project/beta" };
        await _client.PostAsJsonAsync("/hooks/session-start", payload);

        var stopPayload = new HookPayload { SessionId = "test-stop-1", LastAssistantMessage = "All done" };
        var response = await _client.PostAsJsonAsync("/hooks/stop", stopPayload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await _client.GetFromJsonAsync<StateResponse>("/api/state");
        var instance = state!.Instances.First(i => i.SessionId == "test-stop-1");
        Assert.Equal("WaitingForInput", instance.State);
    }

    [Fact]
    public async Task PromptSubmit_UpdatesStateToBusy()
    {
        var payload = new HookPayload { SessionId = "test-prompt-1" };
        await _client.PostAsJsonAsync("/hooks/session-start", payload);

        var promptPayload = new HookPayload { SessionId = "test-prompt-1", Prompt = "do work" };
        var response = await _client.PostAsJsonAsync("/hooks/prompt-submit", promptPayload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await _client.GetFromJsonAsync<StateResponse>("/api/state");
        var instance = state!.Instances.First(i => i.SessionId == "test-prompt-1");
        Assert.Equal("Busy", instance.State);
    }

    [Fact]
    public async Task Notification_UpdatesStateToIdle()
    {
        var payload = new HookPayload { SessionId = "test-notify-1" };
        await _client.PostAsJsonAsync("/hooks/session-start", payload);
        await _client.PostAsJsonAsync("/hooks/prompt-submit", new HookPayload { SessionId = "test-notify-1", Prompt = "x" });

        var response = await _client.PostAsJsonAsync("/hooks/notification", new HookPayload { SessionId = "test-notify-1" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await _client.GetFromJsonAsync<StateResponse>("/api/state");
        var instance = state!.Instances.First(i => i.SessionId == "test-notify-1");
        Assert.Equal("Idle", instance.State);
    }

    [Fact]
    public async Task State_ReturnsSelectedInstanceId()
    {
        var payload = new HookPayload { SessionId = "test-selected-1" };
        await _client.PostAsJsonAsync("/hooks/session-start", payload);

        var state = await _client.GetFromJsonAsync<StateResponse>("/api/state");
        Assert.NotNull(state?.SelectedInstanceId);
    }
}

// DTOs for deserialization
file sealed class StateResponse
{
    public string? SelectedInstanceId { get; set; }
    public List<InstanceResponse> Instances { get; set; } = [];
}

file sealed class InstanceResponse
{
    public string Id { get; set; } = "";
    public string? SessionId { get; set; }
    public string? ProjectName { get; set; }
    public string State { get; set; } = "";
}
