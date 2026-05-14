using System.Collections.Concurrent;
using System.Text.Json;
using CommandCentral.Core.Configuration;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommandCentral.Output;

public sealed class PersonalityManager : IPersonalityManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _personalitiesPath;
    private readonly ILogger<PersonalityManager> _logger;
    private readonly ConcurrentDictionary<string, PersonalityConfig?> _cache = new();

    public PersonalityManager(IOptions<CommandCentralOptions> options, ILogger<PersonalityManager> logger)
    {
        _logger = logger;
        _personalitiesPath = ResolvePersonalitiesPath(options.Value.Tts.PersonalitiesPath);

        if (!Directory.Exists(_personalitiesPath))
        {
            Directory.CreateDirectory(_personalitiesPath);
            _logger.LogInformation("Created personalities directory: {Path}", _personalitiesPath);
        }

        Reload();
    }

    public PersonalityConfig? GetForSlot(string slotId)
    {
        return _cache.GetValueOrDefault(slotId);
    }

    public string? ResolveVoiceRefPath(string slotId)
    {
        var config = GetForSlot(slotId);
        if (config?.VoiceRef is null)
            return null;

        var absolutePath = Path.IsPathRooted(config.VoiceRef)
            ? config.VoiceRef
            : Path.Combine(_personalitiesPath, config.VoiceRef);

        if (!File.Exists(absolutePath))
        {
            _logger.LogWarning("Voice ref file not found for slot {Slot}: {Path}", slotId, absolutePath);
            return null;
        }

        return absolutePath;
    }

    public string? ResolveSlotConfigPath(string slotId)
    {
        var filePath = Path.Combine(_personalitiesPath, $"slot-{slotId}.json");
        return File.Exists(filePath) ? filePath : null;
    }

    public void Reload()
    {
        _cache.Clear();

        if (!Directory.Exists(_personalitiesPath))
        {
            _logger.LogDebug("Personalities directory does not exist: {Path}", _personalitiesPath);
            return;
        }

        var files = Directory.GetFiles(_personalitiesPath, "slot-*.json");
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var slotId = fileName.Replace("slot-", "");

            try
            {
                var json = File.ReadAllText(file);
                var config = JsonSerializer.Deserialize<PersonalityConfig>(json, JsonOptions);
                if (config is not null)
                {
                    _cache[slotId] = config;
                    _logger.LogInformation("Loaded personality for slot {Slot}: {Name}", slotId, config.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load personality config: {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} personality configs from {Path}", _cache.Count, _personalitiesPath);
    }

    private static string ResolvePersonalitiesPath(string? configuredPath)
    {
        if (!string.IsNullOrEmpty(configuredPath))
        {
            return Environment.ExpandEnvironmentVariables(configuredPath);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CommandCentral", "personalities");
    }
}
