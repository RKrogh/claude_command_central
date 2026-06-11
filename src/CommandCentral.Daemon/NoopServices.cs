using CommandCentral.Core.Configuration;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Options;

namespace CommandCentral.Daemon;

/// <summary>
/// Noop implementations for headless/test mode where hardware and cloud
/// services are unavailable.
/// </summary>
internal sealed class NoopTtsNotifier : ITtsNotifier
{
    public Task NotifyInstanceReadyAsync(string instanceId, string? voiceProfile = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyDoneAsync(string instanceId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ReadResponseAsync(string text, string? voiceProfile = null, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NoopPersonalityManager : IPersonalityManager
{
    public PersonalityConfig? GetForSlot(string slotId) => null;
    public string? ResolveVoiceRefPath(string slotId) => null;
    public string? ResolveSlotConfigPath(string slotId) => null;
    public void Reload() { }
}

internal sealed class NoopKeystrokeInjector : IKeystrokeInjector
{
    public Task InjectTextAsync(nint windowHandle, string text, CancellationToken ct = default) => Task.CompletedTask;
    public Task InjectTextAndSubmitAsync(nint windowHandle, string text, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Headless/test-mode secret provider: honors an explicit configured secret
/// (so auth can be tested) but never touches the filesystem. No secret
/// configured → auth is not enforced, keeping tests hermetic.
/// </summary>
internal sealed class ConfigHookSecretProvider(IOptions<CommandCentralOptions> options) : IHookSecretProvider
{
    public string? Secret
    {
        get
        {
            var opts = options.Value.HookAuth;
            return opts.Enabled && !string.IsNullOrEmpty(opts.Secret) ? opts.Secret : null;
        }
    }
}

internal sealed class NoopNotificationCacheWarmer : INotificationCacheWarmer
{
    public Task WarmupAsync(string slotId, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// In-memory state store for headless/test mode — state never touches disk.
/// </summary>
internal sealed class NoopStateStore : IStateStore
{
    private readonly object _lock = new();
    private readonly DaemonState _state = new();

    public DaemonState State
    {
        get
        {
            lock (_lock)
            {
                return new DaemonState
                {
                    SelectedInstanceId = _state.SelectedInstanceId,
                    VoiceAssignments = new Dictionary<string, string>(_state.VoiceAssignments)
                };
            }
        }
    }

    public void Update(Action<DaemonState> mutate)
    {
        lock (_lock)
        {
            mutate(_state);
        }
    }
}
