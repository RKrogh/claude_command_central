using System.Net.Http.Json;
using System.Text.Json;
using CommandCentral.Core.Api;

namespace CommandCentral.Tui.Services;

/// <summary>
/// HTTP client for the Command Central daemon. Used for the initial state
/// fetch; live updates arrive via <see cref="DaemonEventStreamClient"/>.
/// </summary>
public sealed class DaemonClient(string baseUrl = "http://localhost:9000") : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl) };

    public async Task<StateSnapshotDto?> GetStateAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<StateSnapshotDto>("/api/state", JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
