using CommandCentral.Tui.Services;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CommandCentral.Tui.Views;

/// <summary>
/// Agent roster drawn as two-line cards:
///   ▸ ❷ openidiom          ⠹ busy
///       W✓ · D:1a2b · 12s
/// The TUI-selected card gets a selection bar; the daemon-selected instance
/// (PTT target) is marked with ▸. Busy agents animate a braille spinner.
/// </summary>
public sealed class AgentListView : FrameView
{
    private const int RowsPerCard = 3; // two content rows + one gap

    private readonly AgentCanvas _canvas;

    public event Action<string>? AgentSelected;

    public AgentListView()
    {
        Title = " AGENTS ";
        ColorScheme = Theme.Pane;

        _canvas = new AgentCanvas(this)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };

        Add(_canvas);
    }

    public void UpdateAgents(
        IReadOnlyList<AgentView> agents,
        string? selectedId,
        string? daemonSelectedId,
        string? pttInstanceId)
    {
        _canvas.Update(agents, selectedId, daemonSelectedId, pttInstanceId);
    }

    public void Tick(int spinnerFrame) => _canvas.Tick(spinnerFrame);

    private void RaiseSelected(string id) => AgentSelected?.Invoke(id);

    private sealed class AgentCanvas(AgentListView owner) : View
    {
        private IReadOnlyList<AgentView> _agents = [];
        private string? _selectedId;
        private string? _daemonSelectedId;
        private string? _pttInstanceId;
        private int _spinnerFrame;
        private int _scrollOffset; // in cards

        private static readonly char[] Spinner = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

        public void Update(IReadOnlyList<AgentView> agents, string? selectedId, string? daemonSelectedId, string? pttInstanceId)
        {
            _agents = agents;
            _selectedId = selectedId;
            _daemonSelectedId = daemonSelectedId;
            _pttInstanceId = pttInstanceId;
            EnsureSelectedVisible();
            SetNeedsDisplay();
        }

        public void Tick(int spinnerFrame)
        {
            _spinnerFrame = spinnerFrame;
            if (_agents.Any(a => a.Info.State == "Busy"))
                SetNeedsDisplay();
        }

        public override void Redraw(Rect bounds)
        {
            Driver.SetAttribute(Theme.Text);
            Clear();

            if (_agents.Count == 0)
            {
                DrawEmptyState(bounds);
                return;
            }

            var now = DateTime.UtcNow;
            var y = 0;
            for (var i = _scrollOffset; i < _agents.Count && y + 1 < bounds.Height; i++)
            {
                DrawCard(_agents[i], y, bounds.Width, now);
                y += RowsPerCard;
            }

            if (_scrollOffset > 0)
                Put(bounds.Width - 2, 0, "▲", Theme.Dim);
            if (_scrollOffset + VisibleCards() < _agents.Count)
                Put(bounds.Width - 2, bounds.Height - 1, "▼", Theme.Dim);
        }

        private void DrawCard(AgentView agent, int y, int width, DateTime nowUtc)
        {
            var info = agent.Info;
            var isSelected = info.Id == _selectedId;
            var stateAttr = Theme.ForState(info.State);

            var nameAttr = isSelected ? Theme.Selected : Theme.Bright;
            var dimAttr = isSelected ? Theme.SelectedDim : Theme.Dim;

            if (isSelected)
            {
                FillRow(y, width, Theme.Selected);
                FillRow(y + 1, width, Theme.Selected);
            }

            // Row 1: daemon-selection marker, slot glyph, name, state
            var marker = info.Id == _daemonSelectedId ? "▸" : " ";
            var col = Put(0, y, $"{marker} ", isSelected ? Theme.Selected : Theme.Accent);
            col = Put(col, y, $"{AgentFormatter.SlotGlyph(info.Id)} ", isSelected ? nameAttr : stateAttr);

            var stateText = StateText(info);
            var name = info.ProjectName ?? info.SessionId ?? "unknown";
            var maxName = Math.Max(1, width - col - stateText.Length - 2);
            if (name.Length > maxName)
                name = name[..Math.Max(1, maxName - 1)] + "…";
            Put(col, y, name, nameAttr);

            Put(width - stateText.Length - 1, y, stateText, isSelected ? nameAttr : stateAttr);

            // Row 2: window binding, desktop, last activity age
            var window = info.WindowBound ? "W✓" : "W✗";
            var detail = $"{window} · {AgentFormatter.FormatDesktop(info.DesktopId)} · {AgentFormatter.RelativeAge(info.LastActivity, nowUtc)}";
            Put(4, y + 1, detail, dimAttr);
        }

        private string StateText(Core.Api.InstanceSnapshotDto info)
        {
            if (info.Id == _pttInstanceId)
                return "⦿ rec";

            return info.State switch
            {
                "Busy" => $"{Spinner[_spinnerFrame % Spinner.Length]} busy",
                "WaitingForInput" => "◍ wait",
                "Idle" => "● idle",
                "Disconnected" => "○ gone",
                _ => info.State.ToLowerInvariant(),
            };
        }

        private void DrawEmptyState(Rect bounds)
        {
            var lines = new[]
            {
                "no agents yet",
                "",
                "start a Claude Code session",
                "and it will register here",
            };
            var y = Math.Max(0, bounds.Height / 2 - 2);
            foreach (var line in lines)
                Put(Math.Max(0, (bounds.Width - line.Length) / 2), y++, line, Theme.Dim);
        }

        public override bool ProcessKey(KeyEvent keyEvent)
        {
            switch (keyEvent.Key)
            {
                case Key.CursorUp or Key.k:
                    MoveSelection(-1);
                    return true;
                case Key.CursorDown or Key.j:
                    MoveSelection(1);
                    return true;
            }

            return base.ProcessKey(keyEvent);
        }

        public override bool MouseEvent(MouseEvent mouseEvent)
        {
            if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
            {
                var index = _scrollOffset + mouseEvent.Y / RowsPerCard;
                if (index >= 0 && index < _agents.Count)
                {
                    owner.RaiseSelected(_agents[index].Info.Id);
                    SetFocus();
                }
                return true;
            }

            return base.MouseEvent(mouseEvent);
        }

        private void MoveSelection(int delta)
        {
            if (_agents.Count == 0)
                return;

            var index = _agents.ToList().FindIndex(a => a.Info.Id == _selectedId);
            var next = Math.Clamp(index < 0 ? 0 : index + delta, 0, _agents.Count - 1);
            owner.RaiseSelected(_agents[next].Info.Id);
        }

        private void EnsureSelectedVisible()
        {
            var index = _agents.ToList().FindIndex(a => a.Info.Id == _selectedId);
            if (index < 0)
                return;

            if (index < _scrollOffset)
                _scrollOffset = index;
            else if (index >= _scrollOffset + VisibleCards())
                _scrollOffset = index - VisibleCards() + 1;
        }

        private int VisibleCards() => Math.Max(1, Bounds.Height / RowsPerCard);

        private void FillRow(int y, int width, Attribute attr)
        {
            if (y >= Bounds.Height)
                return;

            Driver.SetAttribute(attr);
            Move(0, y);
            Driver.AddStr(new string(' ', Math.Max(0, width)));
        }

        private int Put(int col, int y, string text, Attribute attr)
        {
            if (y >= Bounds.Height || col >= Bounds.Width || col < 0)
                return col;

            Driver.SetAttribute(attr);
            Move(col, y);
            var room = Bounds.Width - col;
            Driver.AddStr(text.Length > room ? text[..room] : text);
            return col + text.Length;
        }
    }
}
