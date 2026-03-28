using CommandCentral.Core.Services;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Native;

namespace CommandCentral.Input;

public sealed class KeystrokeInjector(
    IWindowManager windowManager,
    ILogger<KeystrokeInjector> logger) : IKeystrokeInjector
{
    private readonly EventSimulator _simulator = new();

    public async Task InjectTextAsync(nint windowHandle, string text, CancellationToken ct = default)
    {
        logger.LogInformation("Injecting {Count} chars into window 0x{Handle:X}", text.Length, windowHandle);

        if (windowHandle != nint.Zero)
        {
            await windowManager.FocusWindowAsync(windowHandle, ct);
            // Brief delay to let the window come to front
            await Task.Delay(150, ct);
        }

        foreach (var c in text)
        {
            ct.ThrowIfCancellationRequested();
            _simulator.SimulateTextEntry(c.ToString());
            // Small delay between characters for reliability
            await Task.Delay(10, ct);
        }

        logger.LogDebug("Injected {Count} characters", text.Length);
    }
}
