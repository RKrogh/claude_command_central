using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class AgentListView : FrameView
{
    private readonly ListView _list;
    private List<AgentListItem> _agents = [];
    private bool _updating;

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
            if (!_updating && args.Item >= 0 && args.Item < _agents.Count)
                AgentSelected?.Invoke(_agents[args.Item].Id);
        };

        var legend = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Text = "● busy ○ idle ◐ wait  W=window"
        };

        var nav = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = "[S]ettings  [Q]uit"
        };

        Add(_list, legend, nav);
    }

    public void UpdateAgents(IReadOnlyList<AgentListItem> agents, string? selectedId)
    {
        _updating = true;
        try
        {
            _agents = agents.ToList();
            _list.SetSource(_agents.Select(a => a.DisplayText).ToList());

            var index = _agents.FindIndex(a => a.Id == selectedId);
            if (index >= 0)
                _list.SelectedItem = index;
        }
        finally
        {
            _updating = false;
        }
    }
}

public sealed record AgentListItem(string Id, string DisplayText);
