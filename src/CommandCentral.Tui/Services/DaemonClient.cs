using System.Net.Http.Json;

namespace CommandCentral.Tui.Services;

/// <summary>
/// HTTP client for communicating with the Command Central daemon.
/// WebSocket support will be added in Phase 3 for real-time updates.
/// </summary>
public sealed class DaemonClient(string baseUrl = "http://localhost:9000") : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl) };

    public async Task<DaemonState?> GetStateAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<DaemonState>("/api/state", ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class DaemonState
{
    public string? SelectedInstanceId { get; set; }
    public List<InstanceDto> Instances { get; set; } = [];
}

public sealed class InstanceDto
{
    public string Id { get; set; } = "";
    public string? SessionId { get; set; }
    public string? Cwd { get; set; }
    public string? ProjectName { get; set; }
    public string State { get; set; } = "Idle";
    public string? VoiceProfile { get; set; }
    public DateTime LastActivity { get; set; }
}
