using CommandCentral.Tui.Services;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CommandCentral.Tui.Views;

/// <summary>
/// Right-hand pane: state banner, key/value block and a color-coded
/// activity feed (newest first, PageUp/PageDown to scroll).
/// </summary>
public sealed class AgentDetailView : FrameView
{
    private readonly DetailCanvas _canvas;

    public AgentDetailView()
    {
        Title = " AGENT ";
        ColorScheme = Theme.Pane;

        _canvas = new DetailCanvas
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(),
            CanFocus = true,
        };

        Add(_canvas);
    }

    public void UpdateAgent(AgentView? agent)
    {
        Title = agent is null
            ? " AGENT "
            : $" {AgentFormatter.SlotGlyph(agent.Info.Id)} {(agent.Info.ProjectName ?? "unknown").ToUpperInvariant()} ";
        _canvas.Update(agent);
    }

    private sealed class DetailCanvas : View
    {
        private AgentView? _agent;
        private int _scrollOffset;

        public void Update(AgentView? agent)
        {
            if (_agent?.Info.Id != agent?.Info.Id)
                _scrollOffset = 0;

            _agent = agent;
            SetNeedsDisplay();
        }

        public override void Redraw(Rect bounds)
        {
            Driver.SetAttribute(Theme.Text);
            Clear();

            if (_agent is null)
            {
                Put(0, 1, "select an agent on the left", Theme.Dim);
                return;
            }

            var info = _agent.Info;
            var y = 0;

            // State banner
            var stateAttr = Theme.ForState(info.State);
            Put(0, y, "▍", stateAttr);
            Put(2, y, StateLabel(info.State), stateAttr);
            var age = $"last activity {AgentFormatter.RelativeAge(info.LastActivity, DateTime.UtcNow)}";
            Put(bounds.Width - age.Length, y, age, Theme.Dim);
            y += 2;

            // Key/value block
            y = PutKv(y, "session", info.SessionId is { Length: > 16 } s ? s[..16] + "…" : info.SessionId ?? "--");
            y = PutKv(y, "cwd", info.Cwd ?? "--");
            y = PutKv(y, "window",
                info.WindowBound ? $"✓ bound · {info.WindowBindingSource ?? "?"}" : "✗ not bound",
                info.WindowBound ? Theme.Idle : Theme.Waiting);
            y = PutKv(y, "desktop", AgentFormatter.FormatDesktop(info.DesktopId));
            y = PutKv(y, "voice", info.VoiceProfile ?? "(auto)");
            y += 1;

            // Activity feed
            Put(0, y, "ACTIVITY", Theme.AccentDim);
            Driver.SetAttribute(Theme.Dim);
            Move(9, y);
            Driver.AddStr(" " + new string('─', Math.Max(0, bounds.Width - 10)));
            y += 1;

            var entries = _agent.Activity;
            var visible = Math.Max(0, bounds.Height - y);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, entries.Count - visible));

            // Newest first
            for (var i = 0; i < visible && i + _scrollOffset < entries.Count; i++)
            {
                var entry = entries[entries.Count - 1 - i - _scrollOffset];
                var time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                Put(0, y, time, Theme.Dim);
                Put(time.Length + 1, y, Truncate(entry.Message, bounds.Width - time.Length - 1),
                    Theme.ForActivity(entry.Message));
                y++;
            }

            if (_scrollOffset > 0)
                Put(bounds.Width - 7, bounds.Height - 1, $" +{_scrollOffset} ▼", Theme.AccentDim);
        }

        private static string StateLabel(string state) => state switch
        {
            "Busy" => "BUSY · working",
            "WaitingForInput" => "WAITING · needs you",
            "Idle" => "IDLE · ready",
            "Disconnected" => "DISCONNECTED",
            _ => state.ToUpperInvariant(),
        };

        private int PutKv(int y, string key, string value, Attribute? valueAttr = null)
        {
            Put(0, y, $"{key,9}", Theme.Dim);
            Put(11, y, Truncate(value, Bounds.Width - 11), valueAttr ?? Theme.Bright);
            return y + 1;
        }

        public override bool ProcessKey(KeyEvent keyEvent)
        {
            switch (keyEvent.Key)
            {
                case Key.PageUp:
                    _scrollOffset += 5;
                    SetNeedsDisplay();
                    return true;
                case Key.PageDown:
                    _scrollOffset = Math.Max(0, _scrollOffset - 5);
                    SetNeedsDisplay();
                    return true;
            }

            return base.ProcessKey(keyEvent);
        }

        private static string Truncate(string text, int max) =>
            max <= 1 ? "" : text.Length > max ? text[..(max - 1)] + "…" : text;

        private void Put(int col, int y, string text, Attribute attr)
        {
            if (y >= Bounds.Height || col >= Bounds.Width || col < 0)
                return;

            Driver.SetAttribute(attr);
            Move(col, y);
            var room = Bounds.Width - col;
            Driver.AddStr(text.Length > room ? text[..room] : text);
        }
    }
}
