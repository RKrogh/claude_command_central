using CommandCentral.Core.Configuration;
using CommandCentral.Core.Events;
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
    private readonly IEventBus _eventBus;
    private readonly ILogger<HotkeyManager> _logger;

    // Leader key combo — the only globally intercepted key
    private readonly KeyCombo _leaderCombo;
    private readonly int _leaderTimeoutMs;

    // Leader-mode bindings (active only during leader window)
    private readonly Dictionary<KeyCombo, string> _pttBindings = [];
    private readonly Dictionary<KeyCombo, string> _focusBindings = [];
    private readonly KeyCombo _pttSelectedCombo;
    private readonly KeyCombo _cycleCombo;
    private readonly KeyCombo _quickBackCombo;
    private readonly KeyCombo _muteAllCombo;

    // State machine
    private HotkeyState _state = HotkeyState.Idle;
    private Timer? _leaderTimer;
    private KeyCode _pttKeyCode;

    public HotkeyManager(
        PushToTalkHandler pttHandler,
        IInstanceRegistry registry,
        IVirtualDesktopService virtualDesktop,
        DesktopNavigationContext navigationContext,
        IEventBus eventBus,
        IOptions<CommandCentralOptions> options,
        ILogger<HotkeyManager> logger)
    {
        _pttHandler = pttHandler;
        _registry = registry;
        _virtualDesktop = virtualDesktop;
        _navigationContext = navigationContext;
        _eventBus = eventBus;
        _logger = logger;

        var hotkeys = options.Value.Hotkeys;

        _leaderCombo = KeyCombo.Parse(hotkeys.LeaderKey);
        _leaderTimeoutMs = hotkeys.LeaderTimeoutMs;
        _logger.LogDebug("Leader key: {Leader} (timeout: {Timeout}ms)", hotkeys.LeaderKey, _leaderTimeoutMs);

        foreach (var (comboStr, instanceId) in hotkeys.PttBindings)
        {
            if (KeyCombo.TryParse(comboStr, out var combo))
            {
                _pttBindings[combo] = instanceId;
                _logger.LogDebug("Leader PTT binding: {Combo} → instance {Id}", comboStr, instanceId);
            }
            else
            {
                _logger.LogWarning("Invalid PTT binding key combo: {Combo}", comboStr);
            }
        }

        foreach (var (comboStr, instanceId) in hotkeys.FocusBindings)
        {
            if (KeyCombo.TryParse(comboStr, out var combo))
            {
                _focusBindings[combo] = instanceId;
                _logger.LogDebug("Leader Focus binding: {Combo} → instance {Id}", comboStr, instanceId);
            }
            else
            {
                _logger.LogWarning("Invalid Focus binding key combo: {Combo}", comboStr);
            }
        }

        _pttSelectedCombo = KeyCombo.Parse(hotkeys.PttSelectedInstance);
        _cycleCombo = KeyCombo.Parse(hotkeys.CycleInstance);
        _quickBackCombo = KeyCombo.Parse(hotkeys.QuickBack);
        _muteAllCombo = KeyCombo.Parse(hotkeys.MuteAll);

        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Hotkey manager starting (leader: {Leader}, {PttCount} PTT + {FocusCount} Focus bindings)",
            _leaderCombo, _pttBindings.Count, _focusBindings.Count);
        return _hook.RunAsync();
    }

    public void Stop()
    {
        try
        {
            DeactivateLeader();
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

        switch (_state)
        {
            case HotkeyState.Idle:
                HandleIdleKeyPress(e, mask);
                break;

            case HotkeyState.LeaderActive:
                HandleLeaderKeyPress(e, mask);
                break;

            case HotkeyState.PttActive:
                // Suppress all keys while recording
                e.SuppressEvent = true;
                break;
        }
    }

    private void HandleIdleKeyPress(KeyboardHookEventArgs e, EventMask mask)
    {
        // Only the leader combo is intercepted globally
        if (_leaderCombo.Matches(mask, e.Data.KeyCode))
        {
            e.SuppressEvent = true;
            ActivateLeader();
        }
    }

    private void HandleLeaderKeyPress(KeyboardHookEventArgs e, EventMask mask)
    {
        // Check focus bindings first (Shift+N — more specific)
        foreach (var (combo, instanceId) in _focusBindings)
        {
            if (combo.Matches(mask, e.Data.KeyCode))
            {
                e.SuppressEvent = true;
                DeactivateLeader();
                _logger.LogDebug("Leader → Focus instance {Id}", instanceId);
                _ = Task.Run(() => FocusInstanceAsync(instanceId));
                return;
            }
        }

        // Check PTT bindings (1-9 — hold to record)
        foreach (var (combo, instanceId) in _pttBindings)
        {
            if (combo.Matches(mask, e.Data.KeyCode))
            {
                e.SuppressEvent = true;
                _pttKeyCode = e.Data.KeyCode;
                _state = HotkeyState.PttActive;
                CancelLeaderTimer();
                _logger.LogDebug("Leader → PTT instance {Id}", instanceId);
                _ = Task.Run(() => _pttHandler.StartAsync(instanceId));
                return;
            }
        }

        // PTT for selected instance (Space — hold to record)
        if (_pttSelectedCombo.Matches(mask, e.Data.KeyCode))
        {
            e.SuppressEvent = true;
            _pttKeyCode = e.Data.KeyCode;
            _state = HotkeyState.PttActive;
            CancelLeaderTimer();
            _logger.LogDebug("Leader → PTT selected instance");
            _ = Task.Run(() => _pttHandler.StartAsync());
            return;
        }

        // Quick-back (§)
        if (_quickBackCombo.Matches(mask, e.Data.KeyCode))
        {
            e.SuppressEvent = true;
            DeactivateLeader();
            _logger.LogDebug("Leader → Quick-back");
            _ = Task.Run(() => QuickBackAsync());
            return;
        }

        // Cycle selected instance (`)
        if (_cycleCombo.Matches(mask, e.Data.KeyCode))
        {
            e.SuppressEvent = true;
            DeactivateLeader();
            CycleSelectedInstance();
            return;
        }

        // Mute all (M)
        if (_muteAllCombo.Matches(mask, e.Data.KeyCode))
        {
            e.SuppressEvent = true;
            DeactivateLeader();
            _logger.LogDebug("Leader → Mute all");
            // TODO: wire up mute toggle
            return;
        }

        // Escape cancels leader mode
        if (e.Data.KeyCode == KeyCode.VcEscape)
        {
            e.SuppressEvent = true;
            DeactivateLeader();
            return;
        }

        // Unknown key — cancel leader mode, do NOT suppress (let it through)
        DeactivateLeader();
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (_state != HotkeyState.PttActive)
            return;

        var isTargetKey = e.Data.KeyCode == _pttKeyCode;
        var isModifierUp = e.Data.KeyCode is KeyCode.VcLeftControl or KeyCode.VcRightControl
                        or KeyCode.VcLeftShift or KeyCode.VcRightShift
                        or KeyCode.VcLeftAlt or KeyCode.VcRightAlt;

        if (isTargetKey || isModifierUp)
        {
            e.SuppressEvent = true;
            _state = HotkeyState.Idle;
            _logger.LogDebug("PTT released (key: {Key})", e.Data.KeyCode);
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

    private void ActivateLeader()
    {
        _state = HotkeyState.LeaderActive;
        _leaderTimer?.Dispose();
        _leaderTimer = new Timer(OnLeaderTimeout, null, _leaderTimeoutMs, Timeout.Infinite);
        _eventBus.Publish(new DaemonEvent(DaemonEventType.LeaderActivated));
        _logger.LogDebug("Leader mode activated");
    }

    private void DeactivateLeader()
    {
        if (_state != HotkeyState.LeaderActive)
            return;

        _state = HotkeyState.Idle;
        CancelLeaderTimer();
        _eventBus.Publish(new DaemonEvent(DaemonEventType.LeaderDeactivated));
        _logger.LogDebug("Leader mode deactivated");
    }

    private void CancelLeaderTimer()
    {
        _leaderTimer?.Dispose();
        _leaderTimer = null;
    }

    private void OnLeaderTimeout(object? state)
    {
        if (_state == HotkeyState.LeaderActive)
        {
            _logger.LogDebug("Leader mode timed out");
            DeactivateLeader();
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
        _leaderTimer?.Dispose();
        if (!_hook.IsDisposed)
            _hook.Dispose();
    }
}
