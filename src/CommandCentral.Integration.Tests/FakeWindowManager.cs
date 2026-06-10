using CommandCentral.Input.Platform;

namespace CommandCentral.Integration.Tests;

/// <summary>
/// Configurable window manager fake: set <see cref="ForegroundWindow"/> and
/// <see cref="WindowsByTitle"/> to simulate the desktop state.
/// </summary>
internal sealed class FakeWindowManager : IWindowManager
{
    public nint ForegroundWindow { get; set; }
    public Dictionary<string, nint> WindowsByTitle { get; } = [];
    public List<nint> FocusedWindows { get; } = [];

    public Task<nint> GetForegroundWindowAsync(CancellationToken ct = default) =>
        Task.FromResult(ForegroundWindow);

    public Task FocusWindowAsync(nint windowHandle, CancellationToken ct = default)
    {
        FocusedWindows.Add(windowHandle);
        return Task.CompletedTask;
    }

    public Task<nint> FindWindowByTitleAsync(string titlePattern, CancellationToken ct = default)
    {
        var match = WindowsByTitle.FirstOrDefault(kvp =>
            kvp.Key.Contains(titlePattern, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match.Value);
    }

    public Task<IReadOnlyList<WindowInfo>> GetWindowsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WindowInfo>>(
            WindowsByTitle.Select(kvp => new WindowInfo(kvp.Value, kvp.Key, 0)).ToList());
}
