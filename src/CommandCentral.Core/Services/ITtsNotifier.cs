namespace CommandCentral.Core.Services;

public interface ITtsNotifier
{
    Task NotifyInstanceReadyAsync(string instanceId, string? voiceProfile = null, CancellationToken ct = default);
    Task ReadResponseAsync(string text, string? voiceProfile = null, CancellationToken ct = default);
}
