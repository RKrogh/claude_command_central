using CommandCentral.Core.Models;
using CommandCentral.Core.Services;

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
    public void Reload() { }
}

internal sealed class NoopKeystrokeInjector : IKeystrokeInjector
{
    public Task InjectTextAsync(nint windowHandle, string text, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NoopNotificationCacheWarmer : INotificationCacheWarmer
{
    public Task WarmupAsync(string slotId, CancellationToken ct = default) => Task.CompletedTask;
}
