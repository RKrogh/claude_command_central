using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class AgentListView : FrameView
{
    private readonly ListView _list;

    public AgentListView()
    {
        Title = "Agents";

        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };

        var legend = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Text = "● working  ○ idle  ◐ waiting"
        };

        var nav = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = "[S]ettings  [H]otkeys  [Q]uit"
        };

        Add(_list, legend, nav);
    }

    public void UpdateAgents(IReadOnlyList<AgentListItem> agents)
    {
        _list.SetSource(agents.Select(a => a.DisplayText).ToList());
    }
}

public sealed record AgentListItem(string Id, string Name, string StateIcon)
{
    public string DisplayText => $"[{Id}] {Name,-16} {StateIcon}";
}
