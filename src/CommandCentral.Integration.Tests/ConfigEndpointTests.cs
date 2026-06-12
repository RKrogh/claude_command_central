using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CommandCentral.Core.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CommandCentral.Integration.Tests;

public class ConfigEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient CreateClient(string? overridesPath = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("COMMANDCENTRAL_HEADLESS_ONLY", "true");
            if (overridesPath is not null)
                builder.UseSetting("CommandCentral:Persistence:SettingsOverridesPath", overridesPath);
        }).CreateClient();
    }

    [Fact]
    public async Task GetConfig_ExposesLeaderKeyAndDefaults()
    {
        var client = CreateClient();

        var config = await client.GetFromJsonAsync<ConfigDto>("/api/config", Json);

        Assert.NotNull(config);
        Assert.Equal("Ctrl+Shift+Q", config.Hotkeys.LeaderKey);
        Assert.Equal("1-9", config.Hotkeys.PttBindings);
        Assert.Equal("Shift+1-9", config.Hotkeys.FocusBindings);
        Assert.Equal("Ctrl+1-9", config.Hotkeys.ReadResponseBindings);
        Assert.Equal("SherpaOnnx", config.Tts.NotificationEngine);
        Assert.Equal("Voxtral", config.Tts.ResponseEngine);
        Assert.Equal(1500, config.Tts.MaxResponseChars);
    }

    [Fact]
    public async Task PatchConfig_UpdatesEffectiveOptions()
    {
        var client = CreateClient();

        var response = await client.PatchAsJsonAsync("/api/config",
            new ConfigUpdateDto { ResponseEngine = "sherpaonnx", MaxResponseChars = 500 }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ConfigDto>(Json);
        Assert.Equal("SherpaOnnx", updated!.Tts.ResponseEngine); // canonicalized
        Assert.Equal(500, updated.Tts.MaxResponseChars);

        var roundTrip = await client.GetFromJsonAsync<ConfigDto>("/api/config", Json);
        Assert.Equal(500, roundTrip!.Tts.MaxResponseChars);
    }

    [Theory]
    [InlineData("""{"responseEngine":"Festival"}""")]
    [InlineData("""{"notificationEngine":"ElevenLabs"}""")]
    [InlineData("""{"maxResponseChars":-1}""")]
    public async Task PatchConfig_RejectsInvalidValues(string body)
    {
        var client = CreateClient();

        var response = await client.PatchAsync("/api/config",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchConfig_PersistsOverridesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccc-overrides-{Guid.NewGuid():N}.json");
        try
        {
            var client = CreateClient(overridesPath: path);

            var response = await client.PatchAsJsonAsync("/api/config",
                new ConfigUpdateDto { NotificationEngine = "Voxtral" }, Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.True(File.Exists(path));
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var tts = doc.RootElement.GetProperty("CommandCentral").GetProperty("Tts");
            Assert.Equal("Voxtral", tts.GetProperty("NotificationEngine").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(new[] { "1", "2", "3" }, "1-3")]
    [InlineData(new[] { "Ctrl+1", "Ctrl+2", "Ctrl+9" }, "Ctrl+1, Ctrl+2, Ctrl+9")]
    [InlineData(new[] { "Shift+5" }, "Shift+5")]
    [InlineData(new string[0], "(none)")]
    [InlineData(new[] { "Space", "Tab" }, "Space, Tab")]
    public void SummarizeBindings_CollapsesContiguousDigitRanges(string[] combos, string expected)
    {
        var bindings = combos.ToDictionary(c => c, c => "x");

        Assert.Equal(expected, CommandCentral.Daemon.Endpoints.ConfigEndpoints.SummarizeBindings(bindings));
    }
}
