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
            Text = " PTT: off │ Selected: -- │ Agents: 0/9 │ Audio: □□□□□ │ --:--"
        };

        Add(_content);
    }

    public void Update(bool pttActive, string? selectedId, int agentCount, int maxAgents, int audioLevel)
    {
        var ptt = pttActive ? "● rec" : "off";
        var selected = selectedId ?? "--";
        var audio = new string('■', audioLevel) + new string('□', 5 - audioLevel);
        var time = DateTime.Now.ToString("HH:mm");

        _content.Text = $" PTT: {ptt} │ Selected: #{selected} │ Agents: {agentCount}/{maxAgents} │ Audio: {audio} │ {time}";
    }
}
