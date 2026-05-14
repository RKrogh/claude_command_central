namespace CommandCentral.Core.Models;

/// <summary>
/// Per-slot personality configuration loaded from the user's personalities directory.
/// Each slot (1-9) can have a distinct personality with voice, greeting, and notification phrases.
/// </summary>
public sealed class PersonalityConfig
{
    /// <summary>
    /// Display name for this personality (e.g., "GLaDOS", "Cortana").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Path to a voice reference audio file (WAV/MP3), relative to the personalities directory.
    /// Used by Voxtral for zero-shot voice cloning. Example: "voices/glados-sample.wav"
    /// </summary>
    public string? VoiceRef { get; set; }

    /// <summary>
    /// Personality description injected into the Claude session on start.
    /// Instructs Claude to adopt this persona for all responses.
    /// </summary>
    public string? Personality { get; set; }

    /// <summary>
    /// Short greeting spoken when the session first hooks up.
    /// Supports {slot} placeholder for the slot number.
    /// </summary>
    public string? Greeting { get; set; }

    /// <summary>
    /// Short phrases spoken when Claude finishes a response (Stop hook).
    /// A random entry is picked each time to keep it fresh.
    /// </summary>
    public List<string> DoneNotifications { get; set; } = ["Done."];
}
