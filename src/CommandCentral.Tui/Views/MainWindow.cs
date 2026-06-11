using Terminal.Gui;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Views;

public sealed class MainWindow : Window
{
    private readonly TuiStateStore _store;
    private readonly AgentListView _agentList;
    private readonly AgentDetailView _agentDetail;
    private readonly SettingsView _settings;
    private readonly StatusBarView _statusBar;
    private string? _selectedAgentId;
    private bool _showSettings;

    public MainWindow(TuiStateStore store, string daemonUrl)
    {
        _store = store;
        Title = "Command Central";
        ColorScheme = Colors.Base;

        _agentList = new AgentListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(40),
            Height = Dim.Fill(1)
        };

        _agentDetail = new AgentDetailView
        {
            X = Pos.Right(_agentList),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        _settings = new SettingsView(daemonUrl)
        {
            X = Pos.Right(_agentList),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Visible = false
        };

        _statusBar = new StatusBarView
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };

        _agentList.AgentSelected += OnAgentSelected;

        Add(_agentList, _agentDetail, _settings, _statusBar);

        KeyPress += OnKeyPress;

        // Store mutations happen on background threads (WebSocket reader);
        // marshal rendering onto the UI loop.
        _store.Changed += () => Application.MainLoop?.Invoke(Render);

        // Keep the clock and connection status fresh even when nothing happens.
        Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(1), (_) =>
        {
            RenderStatusBar();
            return true;
        });

        Render();
    }

    private void OnKeyPress(View.KeyEventEventArgs args)
    {
        switch (args.KeyEvent.Key)
        {
            case Key.q or Key.Q:
                Application.RequestStop();
                args.Handled = true;
                break;
            case Key.s or Key.S:
                _showSettings = !_showSettings;
                _agentDetail.Visible = !_showSettings;
                _settings.Visible = _showSettings;
                SetNeedsDisplay();
                args.Handled = true;
                break;
        }
    }

    private void OnAgentSelected(string agentId)
    {
        _selectedAgentId = agentId;
        RenderDetail();
    }

    private void Render()
    {
        var agents = _store.GetAgents();

        if (_selectedAgentId is null || agents.All(a => a.Info.Id != _selectedAgentId))
            _selectedAgentId = _store.SelectedInstanceId ?? agents.FirstOrDefault()?.Info.Id;

        _agentList.UpdateAgents(
            agents.Select(a => new AgentListItem(a.Info.Id, AgentFormatter.FormatListItem(a.Info))).ToList(),
            _selectedAgentId);

        RenderDetail();
        RenderStatusBar();
    }

    private void RenderDetail()
    {
        var agent = _selectedAgentId is null ? null : _store.GetAgent(_selectedAgentId);
        if (agent is null)
        {
            _agentDetail.ShowEmpty();
            return;
        }

        var info = agent.Info;
        _agentDetail.UpdateTitle($" Agent: {info.ProjectName ?? "unknown"} (#{info.Id}) ");
        _agentDetail.UpdateAgent(
            status: info.State,
            project: info.Cwd,
            voice: info.VoiceProfile ?? "(auto)",
            session: info.SessionId is { Length: > 12 } s ? s[..12] : info.SessionId,
            window: info.WindowBound ? "bound" : "not bound",
            desktop: info.DesktopId?.ToString() ?? "unknown");

        // Newest entries on top, like the activity feed in the plan mock.
        _agentDetail.UpdateActivityLog(
            agent.Activity.Select(AgentFormatter.FormatActivityEntry).Reverse().ToList());
    }

    private void RenderStatusBar()
    {
        _statusBar.Update(_store.Connected, _store.SelectedInstanceId, _store.GetAgents().Count);
    }
}
