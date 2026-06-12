using CommandCentral.Core.Api;

namespace CommandCentral.Tui.Services;

/// <summary>
/// One row in the settings pane. Editable rows carry the option values they
/// cycle through (engines) or accept numeric input (Options is null).
/// </summary>
public sealed record SettingRow(
    string Key,
    string Label,
    string Value,
    bool IsHeader = false,
    bool Editable = false,
    string[]? Options = null);

/// <summary>
/// Pure view-model for the settings pane: builds display rows from the
/// daemon config and computes option cycling. No Terminal.Gui dependency,
/// so it is unit-testable.
/// </summary>
public static class SettingsModel
{
    public static readonly string[] EngineOptions = ["SherpaOnnx", "Voxtral", "Disabled"];

    public const string KeyNotificationEngine = "tts.notificationEngine";
    public const string KeyResponseEngine = "tts.responseEngine";
    public const string KeyMaxResponseChars = "tts.maxResponseChars";

    public static IReadOnlyList<SettingRow> Build(ConfigDto? config, string daemonUrl)
    {
        if (config is null)
        {
            return
            [
                new SettingRow("info", "daemon", daemonUrl),
                new SettingRow("info", "config", "not loaded (daemon unreachable?)"),
            ];
        }

        var h = config.Hotkeys;
        return
        [
            new SettingRow("h", "VOICE", "", IsHeader: true),
            new SettingRow(KeyNotificationEngine, "notification engine", config.Tts.NotificationEngine,
                Editable: true, Options: EngineOptions),
            new SettingRow(KeyResponseEngine, "response engine", config.Tts.ResponseEngine,
                Editable: true, Options: EngineOptions),
            new SettingRow(KeyMaxResponseChars, "max response chars", FormatMaxChars(config.Tts.MaxResponseChars),
                Editable: true),
            new SettingRow("stt.language", "STT language", config.SttLanguage),

            new SettingRow("h", "HOTKEYS (restart daemon to change)", "", IsHeader: true),
            new SettingRow("hk.leader", "leader key", $"{h.LeaderKey}  ({h.LeaderTimeoutMs} ms window)"),
            new SettingRow("hk.ptt", "push-to-talk", $"{h.PttBindings} (hold) · {h.PttSelectedInstance} = selected"),
            new SettingRow("hk.read", "read response", $"{h.ReadResponseBindings} · {h.ReadResponseSelected} = selected"),
            new SettingRow("hk.focus", "focus instance", h.FocusBindings),
            new SettingRow("hk.misc", "cycle / back / mute / rebind", $"{h.CycleInstance} / {h.QuickBack} / {h.MuteAll} / {h.RebindWindow}"),

            new SettingRow("h", "SYSTEM", "", IsHeader: true),
            new SettingRow("sys.daemon", "daemon", daemonUrl),
            new SettingRow("sys.maxInstances", "max instances", config.MaxInstances.ToString()),
            new SettingRow("sys.hookAuth", "hook auth", config.HookAuthEnabled ? "enabled" : "disabled"),
        ];
    }

    public static string FormatMaxChars(int value) => value == 0 ? "0 (unlimited)" : value.ToString();

    /// <summary>Next value when cycling an option row; wraps around.</summary>
    public static string NextOption(SettingRow row, int direction = 1)
    {
        if (row.Options is not { Length: > 0 } options)
            return row.Value;

        var index = Array.FindIndex(options, o => o.Equals(row.Value, StringComparison.OrdinalIgnoreCase));
        var next = ((index < 0 ? 0 : index) + direction + options.Length) % options.Length;
        return options[next];
    }

    /// <summary>Maps an edited row to the PATCH payload.</summary>
    public static ConfigUpdateDto? ToUpdate(string key, string value) => key switch
    {
        KeyNotificationEngine => new ConfigUpdateDto { NotificationEngine = value },
        KeyResponseEngine => new ConfigUpdateDto { ResponseEngine = value },
        KeyMaxResponseChars => int.TryParse(value, out var n) && n >= 0
            ? new ConfigUpdateDto { MaxResponseChars = n }
            : null,
        _ => null,
    };
}
