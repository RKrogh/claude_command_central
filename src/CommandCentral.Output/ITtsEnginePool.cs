using TextToVoice.Core;

namespace CommandCentral.Output;

/// <summary>
/// Provides per-slot TTS engines. Returns null when TTS is unavailable
/// (engine disabled, local model missing, API key missing) — callers must
/// treat null as "skip speech", never as an error.
/// </summary>
public interface ITtsEnginePool
{
    /// <summary>
    /// Gets or creates the TTS engine for a slot, or null if TTS is unavailable.
    /// </summary>
    ITtsEngine? GetOrCreate(string slotId);

    /// <summary>
    /// Stable key describing the voice a slot resolves to, used to invalidate
    /// cached notification audio when the voice changes.
    /// </summary>
    string GetVoiceCacheKey(string slotId);

    /// <summary>
    /// Logs one-time startup diagnostics: which engine is configured, whether
    /// it is usable, and exactly what to do if it is not. Never throws.
    /// </summary>
    void LogStartupDiagnostics();
}
