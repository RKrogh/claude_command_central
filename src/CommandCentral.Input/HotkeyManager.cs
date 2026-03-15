using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpHook;
using SharpHook.Data;

namespace CommandCentral.Input;

public sealed class HotkeyManager : IDisposable
{
    private readonly TaskPoolGlobalHook _hook;
    private readonly PushToTalkHandler _pttHandler;
    private readonly IInstanceRegistry _registry;
    private readonly ILogger<HotkeyManager> _logger;

    // Parsed bindings: combo → instance ID
    private readonly Dictionary<KeyCombo, string> _pttBindings = [];
    private readonly KeyCombo _pttSelectedCombo;
    private readonly KeyCombo _cycleCombo;

    private bool _pttActive;
    private string? _pttTargetInstance;
    // Track which key started PTT so we release on the correct key
    private KeyCode _pttKeyCode;

    public HotkeyManager(
        PushToTalkHandler pttHandler,
        IInstanceRegistry registry,
        IOptions<CommandCentralOptions> options,
        ILogger<HotkeyManager> logger)
    {
        _pttHandler = pttHandler;
        _registry = registry;
        _logger = logger;

        var hotkeys = options.Value.Hotkeys;

        // Parse PTT bindings from config
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

        _pttSelectedCombo = KeyCombo.Parse(hotkeys.PttSelectedInstance);
        _cycleCombo = KeyCombo.Parse(hotkeys.CycleInstance);

        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Hotkey manager starting ({Count} PTT bindings)", _pttBindings.Count);
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
            // Check configured PTT bindings (e.g. Ctrl+1 → instance "1")
            foreach (var (combo, instanceId) in _pttBindings)
            {
                if (combo.Matches(mask, e.Data.KeyCode))
                {
                    _pttActive = true;
                    _pttTargetInstance = instanceId;
                    _pttKeyCode = e.Data.KeyCode;
                    _ = _pttHandler.StartAsync(instanceId);
                    return;
                }
            }

            // PTT for selected instance (e.g. Ctrl+Space)
            if (_pttSelectedCombo.Matches(mask, e.Data.KeyCode))
            {
                _pttActive = true;
                _pttTargetInstance = null;
                _pttKeyCode = e.Data.KeyCode;
                _ = _pttHandler.StartAsync();
                return;
            }
        }

        // Cycle selected instance (e.g. Ctrl+BackQuote)
        if (_cycleCombo.Matches(mask, e.Data.KeyCode))
        {
            CycleSelectedInstance();
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_pttActive)
            return;

        // Release PTT when the key that started it is released
        if (e.Data.KeyCode == _pttKeyCode)
        {
            _pttActive = false;
            _pttTargetInstance = null;
            _ = _pttHandler.StopAsync();
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
