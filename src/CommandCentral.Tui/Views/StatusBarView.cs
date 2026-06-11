using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class StatusBarView : View
{
    private readonly Label _content;

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

    public void Update(bool connected, string? selectedId, int agentCount)
    {
        var time = DateTime.Now.ToString("HH:mm");

        if (!connected)
        {
            _content.Text = $" ✕ Daemon not reachable │ Retrying... │ {time}";
            return;
        }

        var selected = selectedId ?? "--";
        _content.Text = $" ● Connected │ Selected: #{selected} │ Agents: {agentCount} │ {time}";
    }
}
