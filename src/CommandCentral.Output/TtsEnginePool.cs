using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextToVoice.Core;

namespace CommandCentral.Output;

/// <summary>
/// Routes notification TTS to the configured backend:
/// "SherpaOnnx" (local Piper models, default), "Voxtral" (Mistral cloud),
/// or "Disabled"/"None". Unknown values disable TTS with a single warning.
/// </summary>
public sealed class TtsEnginePool(
    IOptions<CommandCentralOptions> options,
    SherpaOnnxEnginePool sherpaPool,
    VoxtralEnginePool voxtralPool,
    IPersonalityManager personalityManager,
    ILogger<TtsEnginePool> logger) : ITtsEnginePool
{
    private enum EngineKind { Disabled, SherpaOnnx, Voxtral, Unknown }

    private int _warnedUnknownEngine;

    public ITtsEngine? GetOrCreate(string slotId)
    {
        switch (ConfiguredKind)
        {
            case EngineKind.SherpaOnnx:
                return sherpaPool.GetOrCreate(slotId);
            case EngineKind.Voxtral:
                return voxtralPool.GetOrCreate(slotId);
            case EngineKind.Disabled:
                return null;
            default:
                WarnOnceUnknownEngine();
                return null;
        }
    }

    public string GetVoiceCacheKey(string slotId) => ConfiguredKind switch
    {
        EngineKind.SherpaOnnx => $"sherpa:{sherpaPool.ResolveVoiceKey(slotId)}",
        EngineKind.Voxtral => personalityManager.ResolveVoiceRefPath(slotId)
            ?? $"voxtral:{options.Value.Voxtral.DefaultVoiceId}",
        _ => "none"
    };

    public void LogStartupDiagnostics()
    {
        try
        {
            var opts = options.Value;
            switch (ConfiguredKind)
            {
                case EngineKind.Disabled:
                    logger.LogInformation(
                        "TTS notifications disabled via config (CommandCentral:Tts:NotificationEngine = '{Engine}')",
                        opts.Tts.NotificationEngine);
                    break;

                case EngineKind.SherpaOnnx:
                    var voices = PiperModelLocator.ListAvailableVoices(opts.LocalTts.ModelsDir);
                    if (voices.Count > 0)
                    {
                        logger.LogInformation(
                            "TTS notifications: local sherpa-onnx engine, {Count} voice model(s) in {Dir}: {Voices}",
                            voices.Count, opts.LocalTts.ModelsDir, string.Join(", ", voices));
                    }
                    else
                    {
                        logger.LogWarning("{Help}",
                            SherpaOnnxEnginePool.BuildMissingModelHelp(opts.LocalTts.ModelsDir, opts.LocalTts.DefaultVoice));
                    }
                    break;

                case EngineKind.Voxtral:
                    if (string.IsNullOrEmpty(opts.Voxtral.ApiKey))
                    {
                        logger.LogWarning(
                            "TTS notifications: Voxtral (cloud) configured but no API key set. TTS is disabled " +
                            "(the daemon runs fine without it). Set the key with: " +
                            "dotnet user-secrets set \"CommandCentral:Voxtral:ApiKey\" \"<your-mistral-api-key>\" " +
                            "--project src/CommandCentral.Daemon/ — or switch to the local engine " +
                            "(CommandCentral:Tts:NotificationEngine = 'SherpaOnnx')");
                    }
                    else
                    {
                        logger.LogInformation(
                            "TTS notifications: Voxtral (cloud) engine, model {Model}", opts.Voxtral.ModelId);
                    }
                    break;

                default:
                    WarnOnceUnknownEngine();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Diagnostics must never take the daemon down.
            logger.LogError(ex, "TTS startup diagnostics failed");
        }
    }

    private EngineKind ConfiguredKind => options.Value.Tts.NotificationEngine.Trim() switch
    {
        "" => EngineKind.Disabled,
        var e when e.Equals("Disabled", StringComparison.OrdinalIgnoreCase) => EngineKind.Disabled,
        var e when e.Equals("None", StringComparison.OrdinalIgnoreCase) => EngineKind.Disabled,
        var e when e.Equals("SherpaOnnx", StringComparison.OrdinalIgnoreCase) => EngineKind.SherpaOnnx,
        var e when e.Equals("Voxtral", StringComparison.OrdinalIgnoreCase) => EngineKind.Voxtral,
        _ => EngineKind.Unknown
    };

    private void WarnOnceUnknownEngine()
    {
        if (Interlocked.Exchange(ref _warnedUnknownEngine, 1) == 0)
        {
            logger.LogWarning(
                "Unknown TTS notification engine '{Engine}' — TTS notifications disabled. " +
                "Valid values: SherpaOnnx, Voxtral, Disabled",
                options.Value.Tts.NotificationEngine);
        }
    }
}
