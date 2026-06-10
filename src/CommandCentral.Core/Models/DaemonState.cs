namespace CommandCentral.Core.Models;

/// <summary>
/// User-tuned runtime state that survives daemon restarts.
/// Persisted as a JSON file by <see cref="Services.JsonStateStore"/>.
/// </summary>
public sealed class DaemonState
{
    /// <summary>
    /// The slot ID the user last selected (via cycle hotkey or TUI).
    /// Restored as the preferred selection when that slot re-registers.
    /// </summary>
    public string? SelectedInstanceId { get; set; }

    /// <summary>
    /// Slot ID → voice name assignments, so each slot keeps its voice across restarts.
    /// </summary>
    public Dictionary<string, string> VoiceAssignments { get; set; } = new();
}
