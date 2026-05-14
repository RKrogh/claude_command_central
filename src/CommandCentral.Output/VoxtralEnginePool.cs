using System.Collections.Concurrent;
using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextToVoice.Core;
using TextToVoice.Engines.Voxtral;

namespace CommandCentral.Output;

/// <summary>
/// Manages per-slot VoxtralEngine instances, each configured with the slot's
/// voice reference audio for zero-shot voice cloning.
/// Engines are created lazily on first use and cached for the lifetime of the slot.
/// </summary>
public sealed class VoxtralEnginePool : IDisposable
{
    private readonly ConcurrentDictionary<string, VoxtralEngine> _engines = new();
    private readonly CommandCentralOptions _options;
    private readonly IPersonalityManager _personalityManager;
    private readonly ILogger<VoxtralEnginePool> _logger;
    private bool _disposed;

    public VoxtralEnginePool(
        IOptions<CommandCentralOptions> options,
        IPersonalityManager personalityManager,
        ILogger<VoxtralEnginePool> logger)
    {
        _options = options.Value;
        _personalityManager = personalityManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets or creates a VoxtralEngine for the given slot.
    /// If the slot has a personality with a voice ref, the engine uses voice cloning.
    /// Otherwise falls back to the default Voxtral voice preset.
    /// Returns null if Voxtral is not configured (no API key).
    /// </summary>
    public VoxtralEngine? GetOrCreate(string slotId)
    {
        if (string.IsNullOrEmpty(_options.Voxtral.ApiKey))
        {
            _logger.LogWarning("Voxtral API key not configured; cannot create engine for slot {Slot}", slotId);
            return null;
        }

        return _engines.GetOrAdd(slotId, CreateEngine);
    }

    /// <summary>
    /// Removes and disposes the engine for a slot (e.g., when personality config changes).
    /// </summary>
    public void Evict(string slotId)
    {
        if (_engines.TryRemove(slotId, out var engine))
        {
            engine.Dispose();
            _logger.LogDebug("Evicted Voxtral engine for slot {Slot}", slotId);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var engine in _engines.Values)
            engine.Dispose();

        _engines.Clear();
    }

    private VoxtralEngine CreateEngine(string slotId)
    {
        var voxtralConfig = _options.Voxtral;
        var voiceRefPath = _personalityManager.ResolveVoiceRefPath(slotId);

        var engineOptions = new TextToVoice.Engines.Voxtral.VoxtralOptions
        {
            ApiKey = voxtralConfig.ApiKey!,
            ModelId = voxtralConfig.ModelId,
            Stream = voxtralConfig.Stream,
            ResponseFormat = voxtralConfig.ResponseFormat,
            VoiceId = voxtralConfig.DefaultVoiceId,
            RefAudioPath = voiceRefPath,
            OnWarning = msg => _logger.LogWarning("Voxtral: {Warning}", msg)
        };

        _logger.LogInformation(
            "Created Voxtral engine for slot {Slot} (voice cloning: {Cloning})",
            slotId, voiceRefPath is not null);

        return new VoxtralEngine(engineOptions);
    }
}
