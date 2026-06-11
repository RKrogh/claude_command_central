using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CommandCentral.Tui.Views;

/// <summary>
/// One-line brand header: app name, daemon URL, live activity badges
/// (REC while PTT is recording, TTS while speaking, LEADER while the
/// leader-key window is open), connection dot and clock on the right.
/// </summary>
public sealed class HeaderView : View
{
    private readonly string _daemonUrl;

    private bool _connected;
    private string? _pttInstanceId;
    private string? _ttsInstanceId;
    private bool _leaderActive;
    private int _spinnerFrame;

    private static readonly char[] Spinner = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

    public HeaderView(string daemonUrl)
    {
        _daemonUrl = daemonUrl.Replace("http://", "").Replace("https://", "");
        ColorScheme = Theme.Canvas;
    }

    public void Update(bool connected, string? pttInstanceId, string? ttsInstanceId, bool leaderActive)
    {
        _connected = connected;
        _pttInstanceId = pttInstanceId;
        _ttsInstanceId = ttsInstanceId;
        _leaderActive = leaderActive;
        SetNeedsDisplay();
    }

    public void Tick(int spinnerFrame)
    {
        _spinnerFrame = spinnerFrame;
        SetNeedsDisplay();
    }

    public override void Redraw(Rect bounds)
    {
        Driver.SetAttribute(Theme.Text);
        Clear();

        // Left: brand + daemon URL
        var col = 1;
        col = Put(col, "⌘ ", Theme.Accent);
        col = Put(col, "COMMAND CENTRAL", Theme.Bright);
        col = Put(col, "  ", Theme.Text);
        col = Put(col, _daemonUrl, Theme.Dim);

        // Right side, composed right-to-left: clock, connection, badges
        var clock = DateTime.Now.ToString(" HH:mm:ss ");
        var right = bounds.Width - clock.Length;
        Put(right, clock, Theme.Dim);

        right -= 2;
        Put(right, _connected ? "●" : Spinner[_spinnerFrame % Spinner.Length].ToString(),
            _connected ? Theme.Idle : Theme.Error);

        right = PutBadge(right, _leaderActive, " LEADER ", Theme.LeaderBadge);
        right = PutBadge(right, _ttsInstanceId is not null, $" ♪ TTS {SlotOf(_ttsInstanceId)} ", Theme.TtsBadge);
        PutBadge(right, _pttInstanceId is not null, $" ⦿ REC {SlotOf(_pttInstanceId)} ", Theme.RecBadge);
    }

    private static string SlotOf(string? instanceId) => instanceId is null ? "" : $"#{instanceId}";

    private int Put(int col, string text, Attribute attr)
    {
        if (col >= Bounds.Width)
            return col;

        Driver.SetAttribute(attr);
        Move(col, 0);
        Driver.AddStr(text);
        return col + text.Length;
    }

    private int PutBadge(int rightEdge, bool visible, string text, Attribute attr)
    {
        if (!visible)
            return rightEdge;

        var col = rightEdge - text.Length - 1;
        Put(col, text, attr);
        return col - 1;
    }
}
