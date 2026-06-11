using System.Net;
using System.Net.Http.Json;
using CommandCentral.Core.Models;
using CommandCentral.Daemon;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CommandCentral.Integration.Tests;

public class HookAuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "test-secret-for-hook-auth";

    private HttpClient CreateClient(string? secret = Secret, bool enabled = true)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("COMMANDCENTRAL_HEADLESS_ONLY", "true");
            builder.UseSetting("CommandCentral:HookAuth:Enabled", enabled ? "true" : "false");
            if (secret is not null)
                builder.UseSetting("CommandCentral:HookAuth:Secret", secret);
        }).CreateClient();
    }

    [Fact]
    public async Task Hook_WithoutAuthHeader_IsRejected()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/hooks/stop", new HookPayload { SessionId = "s1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hook_WithWrongSecret_IsRejected()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer wrong-secret");

        var response = await client.PostAsJsonAsync("/hooks/stop", new HookPayload { SessionId = "s1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hook_WithCorrectSecret_IsAccepted()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Secret}");

        var response = await client.PostAsJsonAsync("/hooks/session-start", new HookPayload { SessionId = "auth-ok-1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hook_AuthDisabled_AcceptsWithoutHeader()
    {
        var client = CreateClient(enabled: false);

        var response = await client.PostAsJsonAsync("/hooks/stop", new HookPayload { SessionId = "s1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hook_NoSecretEstablished_AcceptsWithoutHeader()
    {
        // Headless mode without a configured secret: auth is not enforced
        // (the file-based provider only runs in the full daemon).
        var client = CreateClient(secret: null);

        var response = await client.PostAsJsonAsync("/hooks/stop", new HookPayload { SessionId = "s1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthAndApi_AreNotGuarded()
    {
        var client = CreateClient();

        var health = await client.GetAsync("/health");
        var state = await client.GetAsync("/api/state");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer test-secret-for-hook-auth-longer")]
    public void IsAuthorized_RejectsMalformedOrWrongHeaders(string? header)
    {
        Assert.False(HookAuthentication.IsAuthorized(header, Secret));
    }

    [Theory]
    [InlineData("Bearer test-secret-for-hook-auth")]
    [InlineData("Bearer test-secret-for-hook-auth ")]
    public void IsAuthorized_AcceptsExactSecret(string header)
    {
        Assert.True(HookAuthentication.IsAuthorized(header, Secret));
    }
}
