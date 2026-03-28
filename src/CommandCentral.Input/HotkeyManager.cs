using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpHook;
using SharpHook.Data;

namespace CommandCentral.Input;

public sealed class HotkeyManager : IDisposable
{
    private readonly SimpleGlobalHook _hook;
    private readonly PushToTalkHandler _pttHandler;
    private readonly IInstanceRegistry _registry;
    private readonly IVirtualDesktopService _virtualDesktop;
    private readonly DesktopNavigationContext _navigationContext;
    private readonly ILogger<HotkeyManager> _logger;

    // Parsed bindings
    private readonly Dictionary<KeyCombo, string> _pttBindings = [];
    private readonly Dictionary<KeyCombo, string> _focusBindings = [];
    private readonly Dictionary<KeyCombo, string> _focusPttBindings = [];
    private readonly KeyCombo _pttSelectedCombo;
    private readonly KeyCombo _cycleCombo;
    private readonly KeyCombo _quickBackCombo;

    private bool _pttActive;
    private PttMode _pttMode;
    private KeyCode _pttKeyCode;

    public HotkeyManager(
        PushToTalkHandler pttHandler,
        IInstanceRegistry registry,
        IVirtualDesktopService virtualDesktop,
        DesktopNavigationContext navigationContext,
        IOptions<CommandCentralOptions> options,
        ILogger<HotkeyManager> logger)
    {
        _pttHandler = pttHandler;
        _registry = registry;
        _virtualDesktop = virtualDesktop;
        _navigationContext = navigationContext;
        _logger = logger;

        var hotkeys = options.Value.Hotkeys;

        // Parse regular PTT bindings
        foreach (var (comboStr, instanceId) in hotkeys.PttBindings)
        {
            if (KeyCombo.TryParse(comboStr, out var combo))
            {
                _pttBindings[combo] = instanceId;
                _logger.LogDebug("PTT binding: {Combo} → instance {Id}", comboStr, instanceId);
            }
            else
            {
                _logger.LogWarning("Invalid PTT binding key combo: {Combo}", comboStr);
            }
        }

        // Parse focus-only bindings (Shift+N → switch to instance N)
        foreach (var (comboStr, instanceId) in hotkeys.FocusBindings)
        {
            if (KeyCombo.TryParse(comboStr, out var combo))
            {
                _focusBindings[combo] = instanceId;
                _logger.LogDebug("Focus binding: {Combo} → instance {Id}", comboStr, instanceId);
            }
            else
            {
                _logger.LogWarning("Invalid Focus binding key combo: {Combo}", comboStr);
            }
        }

        // Parse Focus PTT bindings (Ctrl+Shift+N → switch + record)
        foreach (var (comboStr, instanceId) in hotkeys.FocusPttBindings)
        {
            if (KeyCombo.TryParse(comboStr, out var combo))
            {
                _focusPttBindings[combo] = instanceId;
                _logger.LogDebug("Focus PTT binding: {Combo} → instance {Id}", comboStr, instanceId);
            }
            else
            {
                _logger.LogWarning("Invalid Focus PTT binding key combo: {Combo}", comboStr);
            }
        }

        _pttSelectedCombo = KeyCombo.Parse(hotkeys.PttSelectedInstance);
        _cycleCombo = KeyCombo.Parse(hotkeys.CycleInstance);
        _quickBackCombo = KeyCombo.Parse(hotkeys.QuickBack);

        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Hotkey manager starting ({PttCount} PTT + {FocusCount} Focus + {FocusPttCount} Focus PTT bindings)",
            _pttBindings.Count, _focusBindings.Count, _focusPttBindings.Count);
        return _hook.RunAsync();
    }

    public void Stop()
    {
        try
        {
            if (_hook.IsRunning)
                _hook.Stop();
            _logger.LogInformation("Hotkey manager stopped");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping hotkey manager");
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var mask = e.RawEvent.Mask;

        if (!_pttActive)
        {
            // Check Focus PTT bindings first (more specific — has Shift modifier)
            foreach (var (combo, instanceId) in _focusPttBindings)
            {
                if (combo.Matches(mask, e.Data.KeyCode))
                {
                    e.SuppressEvent = true;
                    _pttActive = true;
                    _pttMode = PttMode.FocusPtt;
                    _pttKeyCode = e.Data.KeyCode;
                    _logger.LogDebug("Focus PTT key pressed: {Key} for instance {Id}", e.Data.KeyCode, instanceId);
                    _ = Task.Run(() => _pttHandler.StartFocusPttAsync(instanceId));
                    return;
                }
            }

            // Check focus-only bindings (Shift+N → switch desktop, no mic)
            foreach (var (combo, instanceId) in _focusBindings)
            {
                if (combo.Matches(mask, e.Data.KeyCode))
                {
                    e.SuppressEvent = true;
                    _logger.LogDebug("Focus key pressed: {Key} for instance {Id}", e.Data.KeyCode, instanceId);
                    _ = Task.Run(() => FocusInstanceAsync(instanceId));
                    return;
                }
            }

            // Check regular PTT bindings
            foreach (var (combo, instanceId) in _pttBindings)
            {
                if (combo.Matches(mask, e.Data.KeyCode))
                {
                    e.SuppressEvent = true;
                    _pttActive = true;
                    _pttMode = PttMode.Normal;
                    _pttKeyCode = e.Data.KeyCode;
                    _logger.LogDebug("PTT key pressed: {Key} for instance {Id}", e.Data.KeyCode, instanceId);
                    _ = Task.Run(() => _pttHandler.StartAsync(instanceId));
                    return;
                }
            }

            // PTT for selected instance (Ctrl+Space)
            if (_pttSelectedCombo.Matches(mask, e.Data.KeyCode))
            {
                e.SuppressEvent = true;
                _pttActive = true;
                _pttMode = PttMode.Normal;
                _pttKeyCode = e.Data.KeyCode;
                _logger.LogDebug("PTT key pressed: {Key} for selected instance", e.Data.KeyCode);
                _ = Task.Run(() => _pttHandler.StartAsync());
                return;
            }
        }
        else
        {
            // Suppress key repeat while PTT is active
            e.SuppressEvent = true;
            return;
        }

        // Quick-back (Ctrl+Shift+Backquote)
        if (_quickBackCombo.Matches(mask, e.Data.KeyCode))
        {
            e.SuppressEvent = true;
            _logger.LogDebug("Quick-back triggered");
            _ = Task.Run(() => QuickBackAsync());
            return;
        }

        // Cycle selected instance (Ctrl+Backquote)
        if (_cycleCombo.Matches(mask, e.Data.KeyCode))
        {
            e.SuppressEvent = true;
            CycleSelectedInstance();
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_pttActive)
            return;

        var isTargetKey = e.Data.KeyCode == _pttKeyCode;
        var isModifierUp = e.Data.KeyCode is KeyCode.VcLeftControl or KeyCode.VcRightControl
                        or KeyCode.VcLeftShift or KeyCode.VcRightShift
                        or KeyCode.VcLeftAlt or KeyCode.VcRightAlt;

        if (isTargetKey || isModifierUp)
        {
            e.SuppressEvent = true;
            _logger.LogDebug("PTT released (key: {Key}, mode: {Mode})", e.Data.KeyCode, _pttMode);
            _pttActive = false;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _pttHandler.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PTT stop failed");
                }
            });
        }
    }

    private async Task FocusInstanceAsync(string instanceId)
    {
        var instance = _registry.GetById(instanceId);
        if (instance is null || instance.WindowHandle == nint.Zero)
        {
            _logger.LogWarning("Instance {Id} not found or has no window for focus", instanceId);
            return;
        }

        try
        {
            // Save context for quick-back
            if (_virtualDesktop.IsAvailable)
            {
                var context = _virtualDesktop.GetCurrentContext();
                _navigationContext.Push(context);
            }

            await _virtualDesktop.SwitchToDesktopOfWindowAsync(instance.WindowHandle);
            _logger.LogInformation("Focused instance {Id}", instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Focus instance {Id} failed", instanceId);
        }
    }

    private async Task QuickBackAsync()
    {
        var context = _navigationContext.Pop();
        if (context is null)
        {
            _logger.LogDebug("Quick-back: no history");
            return;
        }

        try
        {
            await _virtualDesktop.RestoreContextAsync(context.Value);
            _logger.LogInformation("Quick-back to window 0x{Handle:X}", context.Value.WindowHandle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quick-back failed");
        }
    }

    private void CycleSelectedInstance()
    {
        var instances = _registry.GetAll();
        if (instances.Count == 0)
            return;

        var currentId = _registry.SelectedInstanceId;
        var currentIndex = instances.ToList().FindIndex(i => i.Id == currentId);
        var nextIndex = (currentIndex + 1) % instances.Count;
        _registry.SelectedInstanceId = instances[nextIndex].Id;

        _logger.LogInformation("Selected instance: {Id}", _registry.SelectedInstanceId);
    }

    public void Dispose()
    {
        Stop();
        if (!_hook.IsDisposed)
            _hook.Dispose();
    }
}
