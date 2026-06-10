namespace CommandCentral.Core.Configuration;

public sealed class ServerOptions
{
    public int Port { get; set; } = 9000;
    public string Host { get; set; } = "localhost";
}

public sealed class HotkeyOptions
{
    /// <summary>
    /// Leader key combo that activates hotkey mode.
    /// Only this combo is intercepted globally — all other bindings
    /// are only active during the leader window.
    /// </summary>
    public string LeaderKey { get; set; } = "Ctrl+Shift+Q";

    /// <summary>
    /// Timeout in milliseconds before leader mode auto-deactivates.
    /// </summary>
    public int LeaderTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// PTT bindings active during leader mode: key → instance ID.
    /// Hold to record, release to stop. No modifiers needed.
    /// Default: 1-9 → instance "1"-"9".
    /// </summary>
    public Dictionary<string, string> PttBindings { get; set; } = new()
    {
        ["1"] = "1",
        ["2"] = "2",
        ["3"] = "3",
        ["4"] = "4",
        ["5"] = "5",
        ["6"] = "6",
        ["7"] = "7",
        ["8"] = "8",
        ["9"] = "9"
    };

    /// <summary>
    /// Focus bindings active during leader mode: key → instance ID.
    /// Switches to instance's desktop. Instant action.
    /// Default: Shift+1-9 → instance "1"-"9".
    /// </summary>
    public Dictionary<string, string> FocusBindings { get; set; } = new()
    {
        ["Shift+1"] = "1",
        ["Shift+2"] = "2",
        ["Shift+3"] = "3",
        ["Shift+4"] = "4",
        ["Shift+5"] = "5",
        ["Shift+6"] = "6",
        ["Shift+7"] = "7",
        ["Shift+8"] = "8",
        ["Shift+9"] = "9"
    };

    /// <summary>PTT for currently selected instance (leader mode). Hold to record.</summary>
    public string PttSelectedInstance { get; set; } = "Space";

    /// <summary>Cycle to next selected instance (leader mode). Instant action.</summary>
    public string CycleInstance { get; set; } = "Tab";

    /// <summary>Return to previous desktop (leader mode). Instant action.</summary>
    public string QuickBack { get; set; } = "BackQuote";

    /// <summary>Mute/unmute all audio (leader mode). Instant action.</summary>
    public string MuteAll { get; set; } = "M";
}

public sealed class SttOptions
{
    public string Engine { get; set; } = "Whisper";
    public string ModelPath { get; set; } = "./models/ggml-base.en.bin";
    public string Language { get; set; } = "en";
}

public sealed class TtsOptions
{
    public string NotificationEngine { get; set; } = "Voxtral";
    public string ResponseEngine { get; set; } = "Voxtral";
    public Dictionary<string, VoiceOptions> Voices { get; set; } = new();

    /// <summary>
    /// Path to the personalities directory. Supports environment variables.
    /// Default: %APPDATA%\CommandCentral\personalities
    /// </summary>
    public string? PersonalitiesPath { get; set; }
}

public sealed class VoxtralOptions
{
    /// <summary>
    /// Mistral API key. Required for Voxtral engine. Use user-secrets in production.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Voxtral model ID. Default: voxtral-mini-tts-2603.
    /// </summary>
    public string ModelId { get; set; } = "voxtral-mini-tts-2603";

    /// <summary>
    /// Enable SSE streaming for lower time-to-first-byte.
    /// </summary>
    public bool Stream { get; set; } = true;

    /// <summary>
    /// Default voice ID when no personality voice ref is configured.
    /// </summary>
    public string DefaultVoiceId { get; set; } = "gb_jane_neutral";

    /// <summary>
    /// Output audio format: wav, mp3, pcm, flac, opus.
    /// </summary>
    public string ResponseFormat { get; set; } = "wav";
}

public sealed class VoiceOptions
{
    public string Name { get; set; } = "";
    public string Engine { get; set; } = "Voxtral";
}

public sealed class PersistenceOptions
{
    /// <summary>
    /// Path to the daemon state file (selected instance, voice assignments).
    /// Supports environment variables. Default: %LOCALAPPDATA%\CommandCentral\state.json
    /// </summary>
    public string? StateFilePath { get; set; }
}

public sealed class InstanceOptions
{
    public int MaxInstances { get; set; } = 25;
    public bool AutoAssignVoices { get; set; } = true;
}

public sealed class CommandCentralOptions
{
    public ServerOptions Server { get; set; } = new();
    public HotkeyOptions Hotkeys { get; set; } = new();
    public SttOptions Stt { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
    public VoxtralOptions Voxtral { get; set; } = new();
    public InstanceOptions Instances { get; set; } = new();
    public PersistenceOptions Persistence { get; set; } = new();
}
