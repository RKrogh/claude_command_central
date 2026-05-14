using CommandCentral.Core.Models;

namespace CommandCentral.Core.Services;

/// <summary>
/// Loads and manages per-slot personality configurations from the user's personalities directory.
/// </summary>
public interface IPersonalityManager
{
    /// <summary>
    /// Gets the personality config for a slot, or null if no config exists.
    /// </summary>
    PersonalityConfig? GetForSlot(string slotId);

    /// <summary>
    /// Resolves the absolute path to a voice reference audio file for a slot.
    /// Returns null if the slot has no voice ref or the file doesn't exist.
    /// </summary>
    string? ResolveVoiceRefPath(string slotId);

    /// <summary>
    /// Reloads all personality configs from disk.
    /// </summary>
    void Reload();
}
