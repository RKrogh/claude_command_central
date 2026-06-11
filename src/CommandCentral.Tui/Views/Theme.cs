using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CommandCentral.Tui.Views;

/// <summary>
/// Central palette for the TUI. Dark background, cyan accent, one color per
/// agent state. Attributes are created lazily because they need the console
/// driver, which only exists after Application.Init().
/// </summary>
public static class Theme
{
    // Canvas
    public static Attribute Text => Make(Color.Gray, Color.Black);
    public static Attribute Dim => Make(Color.DarkGray, Color.Black);
    public static Attribute Bright => Make(Color.White, Color.Black);

    // Brand accent
    public static Attribute Accent => Make(Color.BrightCyan, Color.Black);
    public static Attribute AccentDim => Make(Color.Cyan, Color.Black);

    // Agent states
    public static Attribute Busy => Make(Color.BrightCyan, Color.Black);
    public static Attribute Waiting => Make(Color.BrightYellow, Color.Black);
    public static Attribute Idle => Make(Color.BrightGreen, Color.Black);
    public static Attribute Offline => Make(Color.DarkGray, Color.Black);
    public static Attribute Error => Make(Color.BrightRed, Color.Black);

    // Selection bar in the agent list
    public static Attribute Selected => Make(Color.White, Color.Blue);
    public static Attribute SelectedDim => Make(Color.Cyan, Color.Blue);

    // Live badges in the header
    public static Attribute RecBadge => Make(Color.White, Color.Red);
    public static Attribute TtsBadge => Make(Color.White, Color.Magenta);
    public static Attribute LeaderBadge => Make(Color.Black, Color.Brown);

    // Activity log tags
    public static Attribute LogStt => Make(Color.BrightCyan, Color.Black);
    public static Attribute LogTts => Make(Color.BrightMagenta, Color.Black);
    public static Attribute LogPrompt => Make(Color.White, Color.Black);
    public static Attribute LogResponse => Make(Color.BrightGreen, Color.Black);
    public static Attribute LogSession => Make(Color.BrightYellow, Color.Black);
    public static Attribute LogWindow => Make(Color.BrightBlue, Color.Black);

    /// <summary>Scheme for plain containers: dark canvas, no surprises.</summary>
    public static ColorScheme Canvas => new()
    {
        Normal = Text,
        Focus = Text,
        HotNormal = Accent,
        HotFocus = Accent,
        Disabled = Dim,
    };

    /// <summary>Scheme for FrameView panes: dim border, accent title.</summary>
    public static ColorScheme Pane => new()
    {
        Normal = Dim,
        Focus = Dim,
        HotNormal = Accent,
        HotFocus = Accent,
        Disabled = Dim,
    };

    public static Attribute ForState(string state) => state switch
    {
        "Busy" => Busy,
        "WaitingForInput" => Waiting,
        "Idle" => Idle,
        "Disconnected" => Offline,
        _ => Text,
    };

    public static Attribute ForActivity(string message)
    {
        if (message.StartsWith("STT", StringComparison.Ordinal) ||
            message.StartsWith("SttResult", StringComparison.Ordinal))
            return LogStt;
        if (message.StartsWith("Tts", StringComparison.Ordinal) ||
            message.StartsWith("Reading response", StringComparison.Ordinal))
            return LogTts;
        if (message.StartsWith("Prompt", StringComparison.Ordinal))
            return LogPrompt;
        if (message.StartsWith("Response", StringComparison.Ordinal))
            return LogResponse;
        if (message.StartsWith("Session", StringComparison.Ordinal) ||
            message.StartsWith("Personality", StringComparison.Ordinal))
            return LogSession;
        if (message.StartsWith("Window", StringComparison.Ordinal) ||
            message.StartsWith("Text", StringComparison.Ordinal) ||
            message.StartsWith("Desktop", StringComparison.Ordinal))
            return LogWindow;
        return Text;
    }

    private static Attribute Make(Color fg, Color bg) =>
        Application.Driver is null ? new Attribute(fg, bg) : Application.Driver.MakeAttribute(fg, bg);
}
