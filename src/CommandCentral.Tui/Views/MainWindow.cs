using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class MainWindow : Window
{
    private readonly AgentListView _agentList;
    private readonly AgentDetailView _agentDetail;
    private readonly StatusBarView _statusBar;

    public MainWindow()
    {
        Title = "Command Central";
        ColorScheme = Colors.Base;

        _agentList = new AgentListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(35),
            Height = Dim.Fill(1)
        };

        _agentDetail = new AgentDetailView
        {
            X = Pos.Right(_agentList),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        _statusBar = new StatusBarView
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };

        Add(_agentList, _agentDetail, _statusBar);
    }
}
