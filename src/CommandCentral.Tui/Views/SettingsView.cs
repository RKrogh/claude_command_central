using CommandCentral.Core.Api;
using CommandCentral.Tui.Services;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CommandCentral.Tui.Views;

/// <summary>
/// Live settings pane. Editable rows (TTS engines, response length cap) are
/// changed with ←/→/Enter and applied to the daemon immediately via
/// PATCH /api/config; the daemon persists them across restarts. Hotkeys and
/// system values are shown read-only.
/// </summary>
public sealed class SettingsView : FrameView
{
    private readonly SettingsCanvas _canvas;

    /// <summary>Called with the PATCH payload; returns the daemon's error, if any.</summary>
    public Func<ConfigUpdateDto, Task<string?>>? ApplyEdit
    {
        get => _canvas.ApplyEdit;
        set => _canvas.ApplyEdit = value;
    }

    public SettingsView(string daemonUrl)
    {
        Title = " SETTINGS ";
        ColorScheme = Theme.Pane;

        _canvas = new SettingsCanvas(daemonUrl)
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(),
            CanFocus = true,
        };

        Add(_canvas);
    }

    public void UpdateConfig(ConfigDto? config) => _canvas.UpdateConfig(config);

    private sealed class SettingsCanvas(string daemonUrl) : View
    {
        private IReadOnlyList<SettingRow> _rows = SettingsModel.Build(null, daemonUrl);
        private int _selected;
        private string? _feedback;
        private bool _feedbackIsError;

        public Func<ConfigUpdateDto, Task<string?>>? ApplyEdit { get; set; }

        public void UpdateConfig(ConfigDto? config)
        {
            _rows = SettingsModel.Build(config, daemonUrl);
            _selected = Math.Clamp(_selected, 0, Math.Max(0, _rows.Count - 1));
            if (!_rows[_selected].Editable)
                _selected = NextEditable(0, 1);
            SetNeedsDisplay();
        }

        public override void Redraw(Rect bounds)
        {
            Driver.SetAttribute(Theme.Text);
            Clear();

            var y = 0;
            for (var i = 0; i < _rows.Count && y < bounds.Height - 1; i++)
            {
                var row = _rows[i];
                if (row.IsHeader)
                {
                    if (y > 0) y++;
                    Put(0, y, row.Label, Theme.AccentDim);
                    Driver.SetAttribute(Theme.Dim);
                    Move(row.Label.Length + 1, y);
                    Driver.AddStr(" " + new string('─', Math.Max(0, bounds.Width - row.Label.Length - 2)));
                    y++;
                    continue;
                }

                var isSelected = i == _selected;
                Put(0, y, $"{row.Label,28}", isSelected ? Theme.Bright : Theme.Dim);

                var col = 30;
                if (row.Editable && isSelected)
                {
                    col = Put(col, y, "◂ ", Theme.Accent);
                    col = Put(col, y, row.Value, Theme.Selected);
                    Put(col, y, " ▸", Theme.Accent);
                }
                else
                {
                    Put(col, y, row.Value, row.Editable ? Theme.Accent : Theme.Text);
                }

                y++;
            }

            // Footer: edit hints + transient feedback
            var hint = _rows[_selected].Options is not null
                ? "←/→ change · ↑↓ move · applied instantly"
                : _rows[_selected].Editable
                    ? "Enter edit · ↑↓ move · applied instantly"
                    : "↑↓ move between editable values";
            Put(0, bounds.Height - 1, hint, Theme.Dim);

            if (_feedback is not null)
                Put(bounds.Width - _feedback.Length - 1, bounds.Height - 1, _feedback,
                    _feedbackIsError ? Theme.Error : Theme.Idle);
        }

        public override bool ProcessKey(KeyEvent keyEvent)
        {
            switch (keyEvent.Key)
            {
                case Key.CursorUp:
                    _selected = NextEditable(_selected, -1);
                    SetNeedsDisplay();
                    return true;
                case Key.CursorDown:
                    _selected = NextEditable(_selected, 1);
                    SetNeedsDisplay();
                    return true;
                case Key.CursorLeft:
                    Cycle(-1);
                    return true;
                case Key.CursorRight:
                    Cycle(1);
                    return true;
                case Key.Enter:
                    var row = _rows[_selected];
                    if (row.Options is not null)
                        Cycle(1);
                    else if (row.Editable)
                        EditNumber(row);
                    return true;
            }

            return base.ProcessKey(keyEvent);
        }

        private void Cycle(int direction)
        {
            var row = _rows[_selected];
            if (row.Options is null)
                return;

            Apply(row, SettingsModel.NextOption(row, direction));
        }

        private void EditNumber(SettingRow row)
        {
            var field = new TextField(row.Value.Split(' ')[0]) { X = 1, Y = 1, Width = Dim.Fill(1) };
            var ok = new Button("Ok", is_default: true);
            var cancel = new Button("Cancel");
            var dialog = new Dialog($" {row.Label} ", 44, 7, ok, cancel) { ColorScheme = Theme.Pane };
            dialog.Add(new Label { X = 1, Y = 0, Text = "0 = unlimited", ColorScheme = Theme.Canvas }, field);

            ok.Clicked += () =>
            {
                Application.RequestStop(dialog);
                Apply(row, field.Text?.ToString()?.Trim() ?? "");
            };
            cancel.Clicked += () => Application.RequestStop(dialog);

            field.SetFocus();
            Application.Run(dialog);
        }

        private void Apply(SettingRow row, string newValue)
        {
            var update = SettingsModel.ToUpdate(row.Key, newValue);
            if (update is null)
            {
                ShowFeedback("✗ invalid value", isError: true);
                return;
            }

            var apply = ApplyEdit;
            if (apply is null)
                return;

            _ = Task.Run(async () =>
            {
                var error = await apply(update);
                Application.MainLoop?.Invoke(() =>
                    ShowFeedback(error is null ? "✓ saved" : $"✗ {Truncate(error, 60)}", error is not null));
            });
        }

        private void ShowFeedback(string message, bool isError)
        {
            _feedback = message;
            _feedbackIsError = isError;
            SetNeedsDisplay();

            Application.MainLoop?.AddTimeout(TimeSpan.FromSeconds(4), _ =>
            {
                if (_feedback == message)
                {
                    _feedback = null;
                    SetNeedsDisplay();
                }
                return false;
            });
        }

        private int NextEditable(int from, int direction)
        {
            var i = from;
            for (var step = 0; step < _rows.Count; step++)
            {
                i = (i + direction + _rows.Count) % _rows.Count;
                if (_rows[i].Editable)
                    return i;
            }

            return from;
        }

        private static string Truncate(string text, int max) =>
            text.Length > max ? text[..(max - 1)] + "…" : text;

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
