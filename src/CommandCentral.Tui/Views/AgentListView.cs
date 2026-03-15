using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class AgentListView : FrameView
{
    private readonly ListView _list;
    private List<AgentListItem> _agents = [];

    public event Action<string>? AgentSelected;

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

        _list.SelectedItemChanged += (args) =>
        {
            if (args.Item >= 0 && args.Item < _agents.Count)
                AgentSelected?.Invoke(_agents[args.Item].Id);
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
            Text = "[Q]uit"
        };

        Add(_list, legend, nav);
    }

    public void UpdateAgents(IReadOnlyList<AgentListItem> agents)
    {
        _agents = agents.ToList();
        _list.SetSource(_agents.Select(a => a.DisplayText).ToList());
    }
}

public sealed record AgentListItem(string Id, string Name, string StateIcon)
{
    public string DisplayText => $"[{Id}] {Name,-16} {StateIcon}";
}
