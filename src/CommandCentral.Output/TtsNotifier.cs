using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using TextToVoice.Core;

namespace CommandCentral.Output;

public sealed class TtsNotifier(
    ITtsEngine engine,
    ILogger<TtsNotifier> logger) : ITtsNotifier
{
    public async Task NotifyInstanceReadyAsync(string instanceId, string? voiceProfile = null, CancellationToken ct = default)
    {
        try
        {
            if (voiceProfile is not null)
                engine.SetVoice(voiceProfile);

            var text = $"Instance {instanceId} ready";
            await engine.SpeakAsync(text, ct);
            logger.LogDebug("TTS notification: {Text}", text);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS notification failed for instance {Id}", instanceId);
        }
    }

    public async Task ReadResponseAsync(string text, string? voiceProfile = null, CancellationToken ct = default)
    {
        try
        {
            if (voiceProfile is not null)
                engine.SetVoice(voiceProfile);

            await engine.SpeakAsync(text, ct);
            logger.LogDebug("TTS read response ({Length} chars)", text.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS read response failed");
        }
    }
}
