namespace CommandCentral.Core.Services;

/// <summary>
/// Pre-synthesizes notification audio for a slot so playback is instant.
/// </summary>
public interface INotificationCacheWarmer
{
    Task WarmupAsync(string slotId, CancellationToken ct = default);
}
