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
    private int _warnedResponseFallback;

    public ITtsEngine? GetOrCreate(string slotId, TtsPurpose purpose = TtsPurpose.Notification)
    {
        var kind = KindFor(purpose);
        var engine = Resolve(kind, slotId);

        // Response reads degrade to the notification engine (typically local)
        // rather than going silent when the cloud engine is unconfigured.
        if (engine is null && purpose == TtsPurpose.Response)
        {
            var fallbackKind = KindFor(TtsPurpose.Notification);
            if (fallbackKind != kind && fallbackKind != EngineKind.Disabled)
            {
                WarnOnceResponseFallback(kind, fallbackKind);
                engine = Resolve(fallbackKind, slotId);
            }
        }

        return engine;
    }

    private ITtsEngine? Resolve(EngineKind kind, string slotId)
    {
        switch (kind)
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

    public string GetVoiceCacheKey(string slotId) => KindFor(TtsPurpose.Notification) switch
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

            var responseKind = KindFor(TtsPurpose.Response);
            if (responseKind == EngineKind.Voxtral && string.IsNullOrEmpty(opts.Voxtral.ApiKey))
            {
                logger.LogWarning(
                    "TTS response reading: Voxtral (cloud) configured but no API key set — " +
                    "reads fall back to the notification engine. Set the key with: " +
                    "dotnet user-secrets set \"CommandCentral:Voxtral:ApiKey\" \"<your-mistral-api-key>\" " +
                    "--project src/CommandCentral.Daemon/");
            }
            else
            {
                logger.LogInformation(
                    "TTS response reading: {Engine} engine (max {MaxChars} chars per read)",
                    opts.Tts.ResponseEngine, opts.Tts.MaxResponseChars);
            }

            switch (KindFor(TtsPurpose.Notification))
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

    // Config binding can null out the engine names despite the non-nullable
    // declarations; treat null as "not set" and use the option's default.
    private EngineKind KindFor(TtsPurpose purpose)
    {
        var (configured, fallback) = purpose == TtsPurpose.Response
            ? (options.Value.Tts.ResponseEngine, "Voxtral")
            : (options.Value.Tts.NotificationEngine, "SherpaOnnx");
        return ParseKind(configured?.Trim() ?? fallback);
    }

    private static EngineKind ParseKind(string engine) => engine switch
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
                "Unknown TTS engine (notification: '{Notification}', response: '{Response}') — TTS disabled " +
                "for that purpose. Valid values: SherpaOnnx, Voxtral, Disabled",
                options.Value.Tts.NotificationEngine, options.Value.Tts.ResponseEngine);
        }
    }

    private void WarnOnceResponseFallback(EngineKind from, EngineKind to)
    {
        if (Interlocked.Exchange(ref _warnedResponseFallback, 1) == 0)
        {
            logger.LogWarning(
                "Response TTS engine {From} unavailable — falling back to {To} for response reading",
                from, to);
        }
    }
}
