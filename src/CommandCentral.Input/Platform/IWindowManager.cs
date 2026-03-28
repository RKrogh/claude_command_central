namespace CommandCentral.Input.Platform;

public interface IWindowManager
{
    Task<nint> GetForegroundWindowAsync(CancellationToken ct = default);
    Task FocusWindowAsync(nint windowHandle, CancellationToken ct = default);
    Task<nint> FindWindowByTitleAsync(string titlePattern, CancellationToken ct = default);
    Task<IReadOnlyList<WindowInfo>> GetWindowsAsync(CancellationToken ct = default);
}

public sealed record WindowInfo(nint Handle, string Title, int ProcessId);
