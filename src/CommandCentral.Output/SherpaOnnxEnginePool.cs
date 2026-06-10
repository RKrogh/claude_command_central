using System.Collections.Concurrent;
using CommandCentral.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextToVoice.Core;
using TextToVoice.Engines.SherpaOnnx;

namespace CommandCentral.Output;

/// <summary>
/// Manages local sherpa-onnx (Piper VITS) TTS engines, one per voice model.
/// Each slot resolves to a voice via <see cref="VoiceAssigner"/>; if that
/// voice's model is not downloaded, the default voice is used, then any
/// available model. With no models at all, TTS degrades gracefully:
/// GetOrCreate returns null and a single warning explains what to download.
/// </summary>
public sealed class SherpaOnnxEnginePool(
    IOptions<CommandCentralOptions> options,
    VoiceAssigner voiceAssigner,
    ILogger<SherpaOnnxEnginePool> logger) : IDisposable
{
    public const string DownloadBaseUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models";

    private readonly ConcurrentDictionary<string, ITtsEngine?> _enginesByVoice = new();
    private int _warnedNoModels;
    private bool _disposed;

    public ITtsEngine? GetOrCreate(string slotId)
    {
        var model = ResolveModelForSlot(slotId);
        if (model is null)
        {
            WarnOnceNoModels();
            return null;
        }

        return _enginesByVoice.GetOrAdd(model.Voice, _ => CreateEngine(model));
    }

    /// <summary>
    /// The voice a slot currently resolves to, or "none" if no model is available.
    /// </summary>
    public string ResolveVoiceKey(string slotId) =>
        ResolveModelForSlot(slotId)?.Voice ?? "none";

    /// <summary>
    /// True if at least one complete voice model is present in the models directory.
    /// </summary>
    public bool HasAnyModel() =>
        PiperModelLocator.ListAvailableVoices(options.Value.LocalTts.ModelsDir).Count > 0;

    /// <summary>
    /// Human-readable instructions for acquiring a local TTS model.
    /// Used for the warn-once message and startup diagnostics.
    /// </summary>
    public static string BuildMissingModelHelp(string modelsDir, string defaultVoice) =>
        $"No local TTS voice model found in '{modelsDir}'. TTS notifications are disabled (the daemon runs fine without them). " +
        $"To enable: run 'bash scripts/download-tts-model.sh' (WSL) or 'pwsh scripts/download-tts-model.ps1' (Windows), " +
        $"or download {DownloadBaseUrl}/vits-piper-{defaultVoice}.tar.bz2 manually and extract it into '{modelsDir}' " +
        $"so that '{Path.Combine(modelsDir, $"vits-piper-{defaultVoice}", $"{defaultVoice}.onnx")}' exists.";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var engine in _enginesByVoice.Values)
            engine?.Dispose();

        _enginesByVoice.Clear();
    }

    private PiperModelLocator.PiperModel? ResolveModelForSlot(string slotId)
    {
        var local = options.Value.LocalTts;
        var voice = voiceAssigner.AssignVoice(slotId);

        var model = PiperModelLocator.Locate(local.ModelsDir, voice);
        if (model is not null)
            return model;

        // Assigned voice not downloaded — fall back to the default voice
        model = PiperModelLocator.Locate(local.ModelsDir, local.DefaultVoice);
        if (model is not null)
        {
            logger.LogDebug(
                "Voice '{Voice}' for slot {Slot} not downloaded; using default voice '{Default}'",
                voice, slotId, local.DefaultVoice);
            return model;
        }

        // Last resort: any complete model in the directory
        var available = PiperModelLocator.ListAvailableVoices(local.ModelsDir);
        return available.Count > 0
            ? PiperModelLocator.Locate(local.ModelsDir, available[0])
            : null;
    }

    private ITtsEngine? CreateEngine(PiperModelLocator.PiperModel model)
    {
        var local = options.Value.LocalTts;

        try
        {
            var engine = new SherpaOnnxEngine(new SherpaOnnxOptions
            {
                ModelPath = model.ModelPath,
                TokensPath = model.TokensPath,
                DataDir = model.DataDir,
                LengthScale = local.LengthScale,
                NumThreads = local.NumThreads
            });

            logger.LogInformation("Loaded local TTS voice '{Voice}' from {Path}", model.Voice, model.ModelPath);
            return engine;
        }
        catch (Exception ex)
        {
            // Cached as null so a broken model logs once, not on every notification.
            logger.LogError(ex, "Failed to load local TTS model '{Voice}' from {Path}; this voice is disabled",
                model.Voice, model.ModelPath);
            return null;
        }
    }

    private void WarnOnceNoModels()
    {
        if (Interlocked.Exchange(ref _warnedNoModels, 1) == 0)
        {
            var local = options.Value.LocalTts;
            logger.LogWarning("{Help}", BuildMissingModelHelp(local.ModelsDir, local.DefaultVoice));
        }
        else
        {
            logger.LogDebug("Local TTS model still missing; notification skipped");
        }
    }
}
