using System.Runtime.InteropServices;

namespace CommandCentral.Input.Platform;

public sealed partial class WindowsWindowManager : IWindowManager
{
    public Task FocusWindowAsync(nint windowHandle, CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.CompletedTask;

        SetForegroundWindow(windowHandle);
        return Task.CompletedTask;
    }

    public Task<nint> FindWindowByTitleAsync(string titlePattern, CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(nint.Zero);

        nint found = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            var title = GetWindowTitle(hWnd);
            if (title.Contains(titlePattern, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, nint.Zero);

        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<WindowInfo>> GetWindowsAsync(CancellationToken ct = default)
    {
        var windows = new List<WindowInfo>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult<IReadOnlyList<WindowInfo>>(windows);

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title))
                return true;

            GetWindowThreadProcessId(hWnd, out var processId);
            windows.Add(new WindowInfo(hWnd, title, (int)processId));
            return true;
        }, nint.Zero);

        return Task.FromResult<IReadOnlyList<WindowInfo>>(windows);
    }

    private static unsafe string GetWindowTitle(nint hWnd)
    {
        var buffer = stackalloc char[256];
        var length = GetWindowText(hWnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowText(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
