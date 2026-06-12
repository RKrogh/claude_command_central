namespace CommandCentral.Core.Api;

/// <summary>
/// Effective daemon configuration exposed over GET /api/config.
/// Secrets never appear here; binding dictionaries are summarized
/// for display ("1-9" rather than nine entries).
/// </summary>
public sealed record ConfigDto
{
    public required HotkeyConfigDto Hotkeys { get; init; }
    public required TtsConfigDto Tts { get; init; }
    public required string SttLanguage { get; init; }
    public required int MaxInstances { get; init; }
    public required bool HookAuthEnabled { get; init; }
}

public sealed record HotkeyConfigDto
{
    public required string LeaderKey { get; init; }
    public required int LeaderTimeoutMs { get; init; }
    public required string PttBindings { get; init; }
    public required string FocusBindings { get; init; }
    public required string ReadResponseBindings { get; init; }
    public required string PttSelectedInstance { get; init; }
    public required string ReadResponseSelected { get; init; }
    public required string CycleInstance { get; init; }
    public required string QuickBack { get; init; }
    public required string MuteAll { get; init; }
    public required string RebindWindow { get; init; }
}

public sealed record TtsConfigDto
{
    public required string NotificationEngine { get; init; }
    public required string ResponseEngine { get; init; }
    public required int MaxResponseChars { get; init; }
}

/// <summary>
/// Runtime-editable subset accepted by PATCH /api/config. Null fields are
/// left unchanged. Hotkeys are deliberately absent: they are parsed once at
/// daemon startup, so editing them requires a restart.
/// </summary>
public sealed record ConfigUpdateDto
{
    public string? NotificationEngine { get; init; }
    public string? ResponseEngine { get; init; }
    public int? MaxResponseChars { get; init; }
}
