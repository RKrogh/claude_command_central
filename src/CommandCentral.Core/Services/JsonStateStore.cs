using System.Text.Json;
using CommandCentral.Core.Models;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Core.Services;

/// <summary>
/// File-backed state store: a single JSON file, loaded once at startup and
/// rewritten on every change. Tolerant of missing or corrupt files — the daemon
/// must never fail to start because of state file issues.
/// </summary>
public sealed class JsonStateStore : IStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly ILogger<JsonStateStore> _logger;
    private readonly DaemonState _state;

    public JsonStateStore(string filePath, ILogger<JsonStateStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        _state = Load(filePath, logger);
    }

    /// <summary>
    /// Default location: %LOCALAPPDATA%\CommandCentral\state.json
    /// (~/.local/share/CommandCentral/state.json on Linux).
    /// </summary>
    public static string DefaultStateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommandCentral", "state.json");

    public string FilePath => _filePath;

    public DaemonState State
    {
        get
        {
            lock (_lock)
            {
                return Snapshot(_state);
            }
        }
    }

    public void Update(Action<DaemonState> mutate)
    {
        lock (_lock)
        {
            mutate(_state);
            Save();
        }
    }

    private static DaemonState Load(string filePath, ILogger logger)
    {
        if (!File.Exists(filePath))
        {
            logger.LogDebug("No state file at {Path}; starting with fresh state", filePath);
            return new DaemonState();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var state = JsonSerializer.Deserialize<DaemonState>(json, JsonOptions);
            if (state is null)
            {
                logger.LogWarning("State file {Path} deserialized to null; starting with fresh state", filePath);
                return new DaemonState();
            }

            logger.LogInformation(
                "Restored daemon state from {Path} (selected: {Selected}, voice assignments: {Voices})",
                filePath, state.SelectedInstanceId ?? "none", state.VoiceAssignments.Count);
            return state;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read state file {Path}; starting with fresh state", filePath);
            return new DaemonState();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Write to a temp file first, then move into place, so a crash
            // mid-write never leaves a truncated state file behind.
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_state, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);

            _logger.LogDebug("Persisted daemon state to {Path}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist daemon state to {Path}", _filePath);
        }
    }

    private static DaemonState Snapshot(DaemonState state) => new()
    {
        SelectedInstanceId = state.SelectedInstanceId,
        VoiceAssignments = new Dictionary<string, string>(state.VoiceAssignments)
    };
}
