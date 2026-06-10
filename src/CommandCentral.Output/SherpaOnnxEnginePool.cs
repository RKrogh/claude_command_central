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
/// voice's model is not downloaded (or fails to load), the default voice is
/// used, then any available model. With no models at all, TTS degrades
/// gracefully: GetOrCreate returns null and a single warning explains what
/// to download.
/// </summary>
/// <remarks>
/// <paramref name="engineFactory"/> is a test seam; production resolves it
/// to null from DI and creates real <see cref="SherpaOnnxEngine"/> instances.
/// </remarks>
public sealed class SherpaOnnxEnginePool(
    IOptions<CommandCentralOptions> options,
    VoiceAssigner voiceAssigner,
    ILogger<SherpaOnnxEnginePool> logger,
    Func<PiperModelLocator.PiperModel, ITtsEngine>? engineFactory = null) : IDisposable
{
    public const string DownloadBaseUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models";

    // Lazy with ExecutionAndPublication guarantees the factory runs at most
    // once per voice, so a concurrent GetOrAdd race can't leak a second
    // native engine that nobody disposes.
    private readonly ConcurrentDictionary<string, Lazy<ITtsEngine?>> _enginesByVoice = new();
    private int _warnedNoModels;
    private volatile bool _disposed;

    public ITtsEngine? GetOrCreate(string slotId)
    {
        if (_disposed)
            return null;

        var anyCandidate = false;
        foreach (var model in ResolveModelCandidates(slotId))
        {
            anyCandidate = true;
            var engine = GetOrCreateEngine(model);
            if (engine is not null)
                return engine;

            // This voice failed to load (error already logged once for it);
            // fall through to the next candidate in the chain.
        }

        if (!anyCandidate)
            WarnOnceNoModels();

        return null;
    }

    /// <summary>
    /// The voice a slot currently resolves to, skipping voices whose models
    /// are known to have failed loading, or "none" if no model is available.
    /// </summary>
    public string ResolveVoiceKey(string slotId)
    {
        foreach (var model in ResolveModelCandidates(slotId))
        {
            if (!IsKnownFailed(model.Voice))
                return model.Voice;
        }

        return "none";
    }

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

        foreach (var lazy in _enginesByVoice.Values)
        {
            if (lazy.IsValueCreated)
                lazy.Value?.Dispose();
        }

        _enginesByVoice.Clear();
    }

    private ITtsEngine? GetOrCreateEngine(PiperModelLocator.PiperModel model)
    {
        if (_disposed)
            return null;

        var lazy = _enginesByVoice.GetOrAdd(
            model.Voice,
            _ => new Lazy<ITtsEngine?>(
                () => CreateEngine(model),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var engine = lazy.Value;

        if (_disposed)
        {
            // Engine creation raced with Dispose — don't let it escape disposal.
            if (_enginesByVoice.TryRemove(model.Voice, out var removed) && removed.IsValueCreated)
                removed.Value?.Dispose();
            return null;
        }

        return engine;
    }

    /// <summary>
    /// Candidate models for a slot, in fallback order: assigned voice →
    /// default voice → any other available model. Duplicates are skipped.
    /// </summary>
    private IEnumerable<PiperModelLocator.PiperModel> ResolveModelCandidates(string slotId)
    {
        var local = options.Value.LocalTts;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var voice = voiceAssigner.AssignVoice(slotId);

        var assigned = PiperModelLocator.Locate(local.ModelsDir, voice);
        if (assigned is not null)
        {
            seen.Add(assigned.Voice);
            yield return assigned;
        }
        else
        {
            logger.LogDebug(
                "Voice '{Voice}' for slot {Slot} not downloaded; falling back to default voice '{Default}'",
                voice, slotId, local.DefaultVoice);
        }

        if (seen.Add(local.DefaultVoice) &&
            PiperModelLocator.Locate(local.ModelsDir, local.DefaultVoice) is { } fallback)
        {
            yield return fallback;
        }

        // Last resort: any complete model in the directory.
        foreach (var available in PiperModelLocator.ListAvailableVoices(local.ModelsDir))
        {
            if (seen.Add(available) &&
                PiperModelLocator.Locate(local.ModelsDir, available) is { } model)
            {
                yield return model;
            }
        }
    }

    private bool IsKnownFailed(string voice) =>
        _enginesByVoice.TryGetValue(voice, out var lazy) && lazy.IsValueCreated && lazy.Value is null;

    private ITtsEngine? CreateEngine(PiperModelLocator.PiperModel model)
    {
        var local = options.Value.LocalTts;

        try
        {
            var engine = engineFactory is not null
                ? engineFactory(model)
                : new SherpaOnnxEngine(new SherpaOnnxOptions
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
