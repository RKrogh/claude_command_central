using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
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
    private readonly ILogger<HotkeyManager> _logger;
    private bool _pttActive;
    private string? _pttTargetInstance;

    public HotkeyManager(
        PushToTalkHandler pttHandler,
        IInstanceRegistry registry,
        IOptions<CommandCentralOptions> options,
        ILogger<HotkeyManager> logger)
    {
        _pttHandler = pttHandler;
        _registry = registry;
        _logger = logger;

        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Hotkey manager starting");
        return _hook.RunAsync();
    }

    public void Stop()
    {
        _hook.Dispose();
        _logger.LogInformation("Hotkey manager stopped");
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var mask = e.RawEvent.Mask;

        // Ctrl+1 through Ctrl+9: PTT for specific instance
        if (mask.HasCtrl() && !_pttActive)
        {
            var instanceId = GetInstanceIdFromKey(e.Data.KeyCode);
            if (instanceId is not null)
            {
                _pttActive = true;
                _pttTargetInstance = instanceId;
                _ = _pttHandler.StartAsync(instanceId);
                e.SuppressEvent = true;
                return;
            }
        }

        // Ctrl+Space: PTT for selected instance
        if (mask.HasCtrl() && e.Data.KeyCode == KeyCode.VcSpace && !_pttActive)
        {
            _pttActive = true;
            _pttTargetInstance = null; // use selected
            _ = _pttHandler.StartAsync();
            e.SuppressEvent = true;
            return;
        }

        // Ctrl+Backtick: Cycle selected instance
        if (mask.HasCtrl() && e.Data.KeyCode == KeyCode.VcBackQuote)
        {
            CycleSelectedInstance();
            e.SuppressEvent = true;
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_pttActive)
            return;

        var releasedInstanceKey = GetInstanceIdFromKey(e.Data.KeyCode);
        var isSpaceRelease = e.Data.KeyCode == KeyCode.VcSpace;

        if ((releasedInstanceKey == _pttTargetInstance) || (isSpaceRelease && _pttTargetInstance is null))
        {
            _pttActive = false;
            _pttTargetInstance = null;
            _ = _pttHandler.StopAsync();
            e.SuppressEvent = true;
        }
    }

    private static string? GetInstanceIdFromKey(KeyCode keyCode) => keyCode switch
    {
        KeyCode.Vc1 => "1",
        KeyCode.Vc2 => "2",
        KeyCode.Vc3 => "3",
        KeyCode.Vc4 => "4",
        KeyCode.Vc5 => "5",
        KeyCode.Vc6 => "6",
        KeyCode.Vc7 => "7",
        KeyCode.Vc8 => "8",
        KeyCode.Vc9 => "9",
        _ => null
    };

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
        _hook.Dispose();
    }
}
