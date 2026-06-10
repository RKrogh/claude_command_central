using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using TextToVoice.Core;

namespace CommandCentral.Output;

public sealed class TtsNotifier(
    ITtsEnginePool enginePool,
    NotificationCache notificationCache,
    IPersonalityManager personalityManager,
    ILogger<TtsNotifier> logger) : ITtsNotifier
{
    private static readonly Random Rng = new();

    public async Task NotifyInstanceReadyAsync(string instanceId, string? voiceProfile = null, CancellationToken ct = default)
    {
        try
        {
            var personality = personalityManager.GetForSlot(instanceId);

            if (personality?.Greeting is not null)
            {
                var greeting = personality.Greeting.Replace("{slot}", instanceId);
                var audioPath = await notificationCache.GetOrSynthesizeAsync(
                    instanceId, greeting, "greeting", ct);

                if (audioPath is not null)
                {
                    await AudioPlayer.PlayAsync(audioPath, ct);
                    logger.LogDebug("Played greeting for slot {Slot}: {Text}", instanceId, greeting);
                    return;
                }
            }

            // Fallback: use engine directly with default voice
            var engine = enginePool.GetOrCreate(instanceId);
            if (engine is null)
            {
                logger.LogDebug("No TTS engine available for slot {Slot}", instanceId);
                return;
            }

            var text = $"Instance {instanceId} ready";
            await engine.SpeakAsync(text, ct);
            logger.LogDebug("TTS notification: {Text}", text);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS notification failed for instance {Id}", instanceId);
        }
    }

    public async Task NotifyDoneAsync(string instanceId, CancellationToken ct = default)
    {
        try
        {
            var personality = personalityManager.GetForSlot(instanceId);

            if (personality is not null && personality.DoneNotifications.Count > 0)
            {
                var index = Rng.Next(personality.DoneNotifications.Count);
                var phrase = personality.DoneNotifications[index];
                var cacheKey = $"done-{index}";

                var audioPath = await notificationCache.GetOrSynthesizeAsync(
                    instanceId, phrase, cacheKey, ct);

                if (audioPath is not null)
                {
                    await AudioPlayer.PlayAsync(audioPath, ct);
                    logger.LogDebug("Played done notification for slot {Slot}: {Text}", instanceId, phrase);
                    return;
                }
            }

            // Fallback: generic notification
            var engine = enginePool.GetOrCreate(instanceId);
            if (engine is null) return;

            await engine.SpeakAsync("Done.", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS done notification failed for instance {Id}", instanceId);
        }
    }

    public async Task ReadResponseAsync(string text, string? voiceProfile = null, CancellationToken ct = default)
    {
        try
        {
            var slotId = voiceProfile ?? "1";
            var engine = enginePool.GetOrCreate(slotId);
            if (engine is null)
            {
                logger.LogWarning("No TTS engine available for reading response");
                return;
            }

            await engine.SpeakAsync(text, ct);
            logger.LogDebug("TTS read response ({Length} chars)", text.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS read response failed");
        }
    }
}
