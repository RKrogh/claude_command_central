using CommandCentral.Core.Api;
using CommandCentral.Core.Configuration;
using Microsoft.Extensions.Options;

namespace CommandCentral.Daemon.Endpoints;

public static class ConfigEndpoints
{
    private static readonly string[] ValidEngines = ["SherpaOnnx", "Voxtral", "Disabled", "None"];

    public static void MapConfigEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/config", (IOptions<CommandCentralOptions> options) =>
            Results.Ok(BuildConfig(options.Value)));

        // Applies live (TtsEnginePool and TtsNotifier read options per call)
        // and persists to the overrides file so it survives restarts.
        api.MapPatch("/config", (
            ConfigUpdateDto update,
            IOptions<CommandCentralOptions> options,
            SettingsOverrideStore overrideStore) =>
        {
            if (Validate(update) is { } problem)
                return Results.BadRequest(new { error = problem });

            var tts = options.Value.Tts;
            if (update.NotificationEngine is not null)
                tts.NotificationEngine = Canonical(update.NotificationEngine);
            if (update.ResponseEngine is not null)
                tts.ResponseEngine = Canonical(update.ResponseEngine);
            if (update.MaxResponseChars is not null)
                tts.MaxResponseChars = update.MaxResponseChars.Value;

            overrideStore.SaveTts(tts);
            return Results.Ok(BuildConfig(options.Value));
        });
    }

    private static string? Validate(ConfigUpdateDto update)
    {
        if (update.NotificationEngine is not null && !IsValidEngine(update.NotificationEngine))
            return $"Unknown notification engine '{update.NotificationEngine}'. Valid: {string.Join(", ", ValidEngines)}";
        if (update.ResponseEngine is not null && !IsValidEngine(update.ResponseEngine))
            return $"Unknown response engine '{update.ResponseEngine}'. Valid: {string.Join(", ", ValidEngines)}";
        if (update.MaxResponseChars is < 0)
            return "MaxResponseChars must be >= 0 (0 = unlimited)";
        return null;
    }

    private static bool IsValidEngine(string engine) =>
        ValidEngines.Any(v => v.Equals(engine.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Canonical(string engine) =>
        ValidEngines.First(v => v.Equals(engine.Trim(), StringComparison.OrdinalIgnoreCase));

    private static ConfigDto BuildConfig(CommandCentralOptions options) => new()
    {
        Hotkeys = new HotkeyConfigDto
        {
            LeaderKey = options.Hotkeys.LeaderKey,
            LeaderTimeoutMs = options.Hotkeys.LeaderTimeoutMs,
            PttBindings = SummarizeBindings(options.Hotkeys.PttBindings),
            FocusBindings = SummarizeBindings(options.Hotkeys.FocusBindings),
            ReadResponseBindings = SummarizeBindings(options.Hotkeys.ReadResponseBindings),
            PttSelectedInstance = options.Hotkeys.PttSelectedInstance,
            ReadResponseSelected = options.Hotkeys.ReadResponseSelected,
            CycleInstance = options.Hotkeys.CycleInstance,
            QuickBack = options.Hotkeys.QuickBack,
            MuteAll = options.Hotkeys.MuteAll,
            RebindWindow = options.Hotkeys.RebindWindow,
        },
        Tts = new TtsConfigDto
        {
            NotificationEngine = options.Tts.NotificationEngine,
            ResponseEngine = options.Tts.ResponseEngine,
            MaxResponseChars = options.Tts.MaxResponseChars,
        },
        SttLanguage = options.Stt.Language,
        MaxInstances = options.Instances.MaxInstances,
        HookAuthEnabled = options.HookAuth.Enabled,
    };

    /// <summary>
    /// Collapses "Ctrl+1".."Ctrl+9" → "Ctrl+1-9" for display. Bindings that
    /// don't follow the prefix+digit convention are joined verbatim.
    /// </summary>
    public static string SummarizeBindings(Dictionary<string, string> bindings)
    {
        if (bindings.Count == 0)
            return "(none)";

        var digits = new List<int>();
        string? prefix = null;
        foreach (var combo in bindings.Keys)
        {
            if (combo.Length == 0 || !char.IsAsciiDigit(combo[^1]))
                return string.Join(", ", bindings.Keys);

            var p = combo[..^1];
            prefix ??= p;
            if (p != prefix)
                return string.Join(", ", bindings.Keys);

            digits.Add(combo[^1] - '0');
        }

        digits.Sort();
        var contiguous = digits.Zip(digits.Skip(1)).All(pair => pair.Second == pair.First + 1);
        return contiguous && digits.Count > 1
            ? $"{prefix}{digits[0]}-{digits[^1]}"
            : string.Join(", ", bindings.Keys);
    }
}
