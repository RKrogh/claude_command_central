using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextToVoice.Core;

namespace CommandCentral.Output;

/// <summary>
/// Caches synthesized notification audio (greetings, done phrases) as WAV files on disk.
/// First synthesis hits the Voxtral API; subsequent plays use the cached file.
/// Invalidation is hash-based: if the phrase text or voice ref changes, the file is re-synthesized.
/// </summary>
public sealed class NotificationCache : INotificationCacheWarmer
{
    private const string ManifestFileName = "manifest.json";

    private readonly VoxtralEnginePool _enginePool;
    private readonly IPersonalityManager _personalityManager;
    private readonly string _personalitiesPath;
    private readonly ILogger<NotificationCache> _logger;
    private readonly ConcurrentDictionary<string, CacheManifest> _manifests = new();

    public NotificationCache(
        VoxtralEnginePool enginePool,
        IPersonalityManager personalityManager,
        IOptions<CommandCentralOptions> options,
        ILogger<NotificationCache> logger)
    {
        _enginePool = enginePool;
        _personalityManager = personalityManager;
        _logger = logger;

        var ttsOptions = options.Value.Tts;
        _personalitiesPath = !string.IsNullOrEmpty(ttsOptions.PersonalitiesPath)
            ? Environment.ExpandEnvironmentVariables(ttsOptions.PersonalitiesPath)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CommandCentral", "personalities");
    }

    /// <summary>
    /// Gets the cached audio file path for a notification phrase.
    /// If not cached or stale, synthesizes it first.
    /// Returns null if synthesis fails or Voxtral is not configured.
    /// </summary>
    public async Task<string?> GetOrSynthesizeAsync(
        string slotId, string phrase, string cacheKey, CancellationToken ct = default)
    {
        var cacheDir = GetSlotCacheDir(slotId);
        var voiceRef = _personalityManager.ResolveVoiceRefPath(slotId);
        var hash = ComputeHash(phrase, voiceRef);
        var manifest = LoadManifest(slotId, cacheDir);

        // Check if cached and still valid
        if (manifest.Entries.TryGetValue(cacheKey, out var entry) && entry.Hash == hash)
        {
            var cachedPath = Path.Combine(cacheDir, entry.FileName);
            if (File.Exists(cachedPath))
            {
                _logger.LogDebug("Cache hit for slot {Slot} key {Key}", slotId, cacheKey);
                return cachedPath;
            }
        }

        // Synthesize
        var engine = _enginePool.GetOrCreate(slotId);
        if (engine is null)
            return null;

        Directory.CreateDirectory(cacheDir);
        var fileName = $"{cacheKey}.wav";
        var filePath = Path.Combine(cacheDir, fileName);

        try
        {
            await engine.SaveToFileAsync(phrase, filePath, ct);

            manifest.Entries[cacheKey] = new CacheEntry(fileName, hash);
            SaveManifest(slotId, cacheDir, manifest);

            _logger.LogInformation("Synthesized and cached: slot {Slot}, key {Key}", slotId, cacheKey);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synthesize notification for slot {Slot}, key {Key}", slotId, cacheKey);
            return null;
        }
    }

    /// <summary>
    /// Pre-synthesizes all notification phrases for a slot in the background.
    /// Call this when a session first connects to a slot.
    /// </summary>
    public async Task WarmupAsync(string slotId, CancellationToken ct = default)
    {
        var personality = _personalityManager.GetForSlot(slotId);
        if (personality is null)
            return;

        var tasks = new List<Task>();

        if (personality.Greeting is not null)
        {
            var greeting = personality.Greeting.Replace("{slot}", slotId);
            tasks.Add(GetOrSynthesizeAsync(slotId, greeting, "greeting", ct));
        }

        for (var i = 0; i < personality.DoneNotifications.Count; i++)
        {
            var index = i;
            tasks.Add(GetOrSynthesizeAsync(slotId, personality.DoneNotifications[index], $"done-{index}", ct));
        }

        await Task.WhenAll(tasks);
        _logger.LogInformation("Warmup complete for slot {Slot}: {Count} phrases cached",
            slotId, tasks.Count);
    }

    /// <summary>
    /// Invalidates all cached audio for a slot (e.g., when personality config changes).
    /// </summary>
    public void Invalidate(string slotId)
    {
        _manifests.TryRemove(slotId, out _);
        var cacheDir = GetSlotCacheDir(slotId);
        if (Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, recursive: true);
            _logger.LogInformation("Invalidated cache for slot {Slot}", slotId);
        }
    }

    private string GetSlotCacheDir(string slotId)
        => Path.Combine(_personalitiesPath, "cache", $"slot-{slotId}");

    private CacheManifest LoadManifest(string slotId, string cacheDir)
    {
        if (_manifests.TryGetValue(slotId, out var cached))
            return cached;

        var manifestPath = Path.Combine(cacheDir, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<CacheManifest>(json) ?? new();
                _manifests[slotId] = manifest;
                return manifest;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cache manifest for slot {Slot}", slotId);
            }
        }

        var empty = new CacheManifest();
        _manifests[slotId] = empty;
        return empty;
    }

    private void SaveManifest(string slotId, string cacheDir, CacheManifest manifest)
    {
        _manifests[slotId] = manifest;
        var manifestPath = Path.Combine(cacheDir, ManifestFileName);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }

    private static string ComputeHash(string phrase, string? voiceRefPath)
    {
        var input = $"{phrase}|{voiceRefPath ?? "default"}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }

    private sealed class CacheManifest
    {
        public Dictionary<string, CacheEntry> Entries { get; set; } = new();
    }

    private sealed record CacheEntry(string FileName, string Hash);
}
