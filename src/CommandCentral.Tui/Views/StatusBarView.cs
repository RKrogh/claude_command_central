using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CommandCentral.Tui.Views;

/// <summary>
/// Bottom bar: connection segment, per-state agent counts, key hints.
/// </summary>
public sealed class StatusBarView : View
{
    private bool _connected;
    private int _busy;
    private int _waiting;
    private int _idle;
    private string? _selectedId;

    public StatusBarView()
    {
        ColorScheme = Theme.Canvas;
    }

    public void Update(bool connected, string? selectedId, int busy, int waiting, int idle)
    {
        _connected = connected;
        _selectedId = selectedId;
        _busy = busy;
        _waiting = waiting;
        _idle = idle;
        SetNeedsDisplay();
    }

    public override void Redraw(Rect bounds)
    {
        Driver.SetAttribute(Theme.Text);
        Clear();

        var col = 1;
        col = _connected
            ? Put(col, "● ONLINE", Theme.Idle)
            : Put(col, "◌ RECONNECTING…", Theme.Error);

        col = Put(col, "  │  ", Theme.Dim);

        if (_busy + _waiting + _idle == 0)
        {
            col = Put(col, "no agents", Theme.Dim);
        }
        else
        {
            if (_busy > 0) col = Put(col, $"{_busy} busy  ", Theme.Busy);
            if (_waiting > 0) col = Put(col, $"{_waiting} waiting  ", Theme.Waiting);
            if (_idle > 0) col = Put(col, $"{_idle} idle  ", Theme.Idle);
        }

        if (_selectedId is not null)
        {
            col = Put(col, "│  PTT target ", Theme.Dim);
            Put(col, $"#{_selectedId}", Theme.Accent);
        }

        // Right: key hints
        var hints = new (string Key, string Label)[] { ("↑↓", "select"), ("S", "settings"), ("Q", "quit") };
        var width = hints.Sum(h => h.Key.Length + h.Label.Length + 3);
        var right = Math.Max(0, bounds.Width - width);
        foreach (var (key, label) in hints)
        {
            right = Put(right, $" {key} ", Theme.Accent);
            right = Put(right, $"{label} ", Theme.Dim);
        }
    }

    private int Put(int col, string text, Attribute attr)
    {
        if (col >= Bounds.Width || col < 0)
            return col;

        Driver.SetAttribute(attr);
        Move(col, 0);
        var room = Bounds.Width - col;
        Driver.AddStr(text.Length > room ? text[..room] : text);
        return col + text.Length;
    }
}
