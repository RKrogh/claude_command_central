using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class StatusBarView : View
{
    private readonly Label _content;
    private bool _connected;

    public StatusBarView()
    {
        _content = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = " Connecting to daemon..."
        };

        Add(_content);
    }

    public void SetConnectionStatus(bool connected)
    {
        _connected = connected;
        if (!connected)
            _content.Text = " ✕ Daemon not reachable │ Retrying...";
    }

    public void Update(bool pttActive, string? selectedId, int agentCount, int maxAgents, int audioLevel)
    {
        var conn = _connected ? "●" : "✕";
        var ptt = pttActive ? "● rec" : "off";
        var selected = selectedId ?? "--";
        var audio = new string('■', audioLevel) + new string('□', 5 - audioLevel);
        var time = DateTime.Now.ToString("HH:mm");

        _content.Text = $" {conn} │ PTT: {ptt} │ Selected: #{selected} │ Agents: {agentCount}/{maxAgents} │ Audio: {audio} │ {time}";
    }
}
