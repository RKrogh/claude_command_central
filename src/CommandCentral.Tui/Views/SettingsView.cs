using Terminal.Gui;

namespace CommandCentral.Tui.Views;

/// <summary>
/// Placeholder settings pane. Editing daemon configuration from the TUI is
/// planned for a later phase; for now this points at the config file.
/// </summary>
public sealed class SettingsView : FrameView
{
    public SettingsView(string daemonUrl)
    {
        Title = " SETTINGS ";
        ColorScheme = Theme.Pane;

        Add(
            new Label { X = 1, Y = 1, Text = $"daemon  {daemonUrl}", ColorScheme = Theme.Canvas },
            new Label { X = 1, Y = 3, Text = "Settings editing is not implemented yet.", ColorScheme = Theme.Canvas },
            new Label { X = 1, Y = 4, Text = "Edit src/CommandCentral.Daemon/appsettings.json and restart the daemon.", ColorScheme = Theme.Canvas },
            new Label { X = 1, Y = 6, Text = "S  back to agent detail", ColorScheme = Theme.Canvas });
    }
}
