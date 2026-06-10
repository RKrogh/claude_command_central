using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Input;

public sealed class WindowBindingService(
    IWindowManager windowManager,
    IInstanceRegistry registry,
    ILogger<WindowBindingService> logger) : IWindowBindingService
{
    /// <summary>Delay to let the terminal title change propagate before marker matching.</summary>
    public TimeSpan MarkerPropagationDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public async Task<nint> BindOnSessionStartAsync(InstanceInfo instance, string? windowMarker, CancellationToken ct = default)
    {
        if (windowMarker is not null)
        {
            // The hook set the terminal title to "cc:<marker>". Best effort:
            // Claude Code usually resets the title before we can match.
            await Task.Delay(MarkerPropagationDelay, ct);

            var markerTag = $"cc:{windowMarker}";
            var handle = await windowManager.FindWindowByTitleAsync(markerTag, ct);
            if (handle != nint.Zero)
            {
                Bind(instance, handle, WindowBindingSource.TitleMarker);
                logger.LogDebug("Matched window by marker '{Marker}'", markerTag);
                return handle;
            }

            logger.LogDebug("Window marker '{Marker}' not found in any window title (expected — Claude Code resets the title)", markerTag);
        }

        // The user just launched Claude Code in a terminal, so that terminal is
        // almost certainly the foreground window. Claim it even if another
        // instance holds the same handle (tabs of one terminal share an HWND);
        // a wrong claim self-heals on the next prompt submit.
        if (await ClaimForegroundAsync(instance, WindowBindingSource.SessionStartForeground, ct))
            return instance.WindowHandle;

        logger.LogWarning("Could not resolve window handle for instance {Id} — will bind on first prompt submit or PTT", instance.Id);
        return nint.Zero;
    }

    public async Task<bool> ClaimForegroundAsync(InstanceInfo instance, WindowBindingSource source, CancellationToken ct = default)
    {
        // Manual bindings are sticky: the user explicitly chose that window,
        // so automatic claims must not overwrite it. IWindowManager has no
        // reliable handle-validity check (GetWindowsAsync only enumerates
        // visible, titled windows), so the binding stays until the user
        // rebinds manually or the instance unregisters.
        if (instance.WindowBindingSource == WindowBindingSource.Manual
            && source != WindowBindingSource.Manual)
        {
            logger.LogDebug("Skipping {Source} claim for instance {Id} — manual binding 0x{Handle:X} is sticky",
                source, instance.Id, instance.WindowHandle);
            return false;
        }

        var foreground = await windowManager.GetForegroundWindowAsync(ct);
        if (foreground == nint.Zero)
        {
            logger.LogDebug("No foreground window available to claim for instance {Id} ({Source})", instance.Id, source);
            return false;
        }

        var sharedWith = registry.GetAll()
            .Where(i => i.Id != instance.Id && i.WindowHandle == foreground)
            .Select(i => i.Id)
            .ToList();
        if (sharedWith.Count > 0)
        {
            logger.LogWarning(
                "Window 0x{Handle:X} is shared by instance {Id} and instance(s) {Others} — " +
                "tabs of the same terminal window cannot be targeted individually",
                foreground, instance.Id, string.Join(", ", sharedWith));
        }

        if (instance.WindowHandle != foreground)
        {
            logger.LogInformation("Instance {Id} window bound: 0x{Old:X} → 0x{New:X} ({Source})",
                instance.Id, instance.WindowHandle, foreground, source);
        }

        Bind(instance, foreground, source);
        return true;
    }

    private static void Bind(InstanceInfo instance, nint handle, WindowBindingSource source)
    {
        instance.WindowHandle = handle;
        instance.WindowBindingSource = source;
        instance.WindowBoundAt = DateTime.UtcNow;
    }
}
