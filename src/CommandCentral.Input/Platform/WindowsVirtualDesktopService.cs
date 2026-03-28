using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Input.Platform;

/// <summary>
/// Virtual desktop management via Windows COM interfaces.
/// Uses the documented IVirtualDesktopManager for desktop detection,
/// and simulated keystrokes (Win+Ctrl+Arrow) as a fallback for switching.
/// </summary>
public sealed partial class WindowsVirtualDesktopService : IVirtualDesktopService, IDisposable
{
    private readonly IWindowManager _windowManager;
    private readonly ILogger<WindowsVirtualDesktopService> _logger;
    private readonly IVirtualDesktopManagerCom? _manager;

    public bool IsAvailable { get; }

    public WindowsVirtualDesktopService(
        IWindowManager windowManager,
        ILogger<WindowsVirtualDesktopService> logger)
    {
        _windowManager = windowManager;
        _logger = logger;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IsAvailable = false;
            return;
        }

        try
        {
            var obj = Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"))!);
            _manager = (IVirtualDesktopManagerCom?)obj;
            IsAvailable = _manager is not null;
            _logger.LogInformation("Virtual desktop service initialized (available: {Available})", IsAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Virtual desktop COM interface not available — desktop-aware features disabled");
            IsAvailable = false;
        }
    }

    public bool IsWindowOnCurrentDesktop(nint hwnd)
    {
        if (_manager is null || hwnd == nint.Zero)
            return true; // Assume same desktop if we can't check

        try
        {
            _manager.IsWindowOnCurrentVirtualDesktop(hwnd, out var isOnCurrent);
            return isOnCurrent;
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "Failed to check virtual desktop for window 0x{Handle:X}", hwnd);
            return true; // Assume same desktop on failure
        }
    }

    public async Task SwitchToDesktopOfWindowAsync(nint hwnd, CancellationToken ct = default)
    {
        if (_manager is null || hwnd == nint.Zero)
            return;

        if (IsWindowOnCurrentDesktop(hwnd))
            return;

        try
        {
            // Try to get the target desktop ID and use COM to switch
            _manager.GetWindowDesktopId(hwnd, out var targetDesktopId);

            if (targetDesktopId != Guid.Empty)
            {
                // MoveWindowToDesktop moves a window TO a desktop, but we want to
                // switch TO the desktop. The trick: focus the window, which causes
                // Windows to switch to its desktop.
                await _windowManager.FocusWindowAsync(hwnd, ct);
                await Task.Delay(300, ct); // Let the desktop switch settle
                _logger.LogDebug("Switched to desktop of window 0x{Handle:X}", hwnd);
                return;
            }
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "COM desktop switch failed, trying focus fallback");
        }

        // Fallback: just try to focus the window — Windows may switch desktops automatically
        await _windowManager.FocusWindowAsync(hwnd, ct);
        await Task.Delay(300, ct);
    }

    public DesktopContext GetCurrentContext()
    {
        if (!IsAvailable || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new DesktopContext(nint.Zero, Guid.Empty);

        var foreground = GetForegroundWindowNative();
        var desktopId = Guid.Empty;

        if (_manager is not null && foreground != nint.Zero)
        {
            try
            {
                _manager.GetWindowDesktopId(foreground, out desktopId);
            }
            catch (COMException)
            {
                // Best effort
            }
        }

        return new DesktopContext(foreground, desktopId);
    }

    public async Task RestoreContextAsync(DesktopContext context, CancellationToken ct = default)
    {
        if (context.WindowHandle == nint.Zero)
            return;

        await SwitchToDesktopOfWindowAsync(context.WindowHandle, ct);
        await _windowManager.FocusWindowAsync(context.WindowHandle, ct);
    }

    public void Dispose()
    {
        if (_manager is not null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Marshal.ReleaseComObject(_manager);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindowNative();

    /// <summary>
    /// Documented COM interface: IVirtualDesktopManager.
    /// Stable across Windows 10 and 11.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    private interface IVirtualDesktopManagerCom
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);
    }
}
