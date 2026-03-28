namespace CommandCentral.Input.Platform;

public readonly record struct DesktopContext(nint WindowHandle, Guid DesktopId);

public interface IVirtualDesktopService
{
    /// <summary>Whether virtual desktop APIs are available on this system.</summary>
    bool IsAvailable { get; }

    /// <summary>Check if a window is on the currently active virtual desktop.</summary>
    bool IsWindowOnCurrentDesktop(nint hwnd);

    /// <summary>Switch to the virtual desktop containing the given window.</summary>
    Task SwitchToDesktopOfWindowAsync(nint hwnd, CancellationToken ct = default);

    /// <summary>Capture the current foreground window and its desktop for later restoration.</summary>
    DesktopContext GetCurrentContext();

    /// <summary>Restore a previously captured desktop context (switch back).</summary>
    Task RestoreContextAsync(DesktopContext context, CancellationToken ct = default);
}
