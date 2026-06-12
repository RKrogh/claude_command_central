using System.Text.Json;
using CommandCentral.Core.Configuration;

namespace CommandCentral.Daemon;

/// <summary>
/// Persists runtime config edits as a JSON overlay that Program.cs layers
/// on top of appsettings.json at startup. Keeps the repo's appsettings
/// pristine while letting TUI edits survive daemon restarts.
/// A null path disables persistence (headless/test mode default).
/// </summary>
public sealed class SettingsOverrideStore(string? path, ILogger<SettingsOverrideStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CommandCentral", "settings-overrides.json");

    public string? FilePath => path;

    public void SaveTts(TtsOptions tts)
    {
        if (path is null)
            return;

        try
        {
            var shape = new Dictionary<string, object>
            {
                ["CommandCentral"] = new Dictionary<string, object>
                {
                    ["Tts"] = new Dictionary<string, object>
                    {
                        ["NotificationEngine"] = tts.NotificationEngine,
                        ["ResponseEngine"] = tts.ResponseEngine,
                        ["MaxResponseChars"] = tts.MaxResponseChars,
                    }
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(shape, JsonOptions));
            logger.LogInformation("Saved settings overrides to {Path}", path);
        }
        catch (Exception ex)
        {
            // A failed save must not fail the API call — the live change
            // already applied; it just won't survive a restart.
            logger.LogError(ex, "Could not persist settings overrides to {Path}", path);
        }
    }
}
