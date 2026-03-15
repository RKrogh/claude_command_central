using Terminal.Gui;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Views;

public sealed class MainWindow : Window
{
    private readonly DaemonClient _client;
    private readonly AgentListView _agentList;
    private readonly AgentDetailView _agentDetail;
    private readonly StatusBarView _statusBar;
    private DaemonState? _lastState;
    private string? _selectedAgentId;

    public MainWindow(DaemonClient client)
    {
        _client = client;
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

        _agentList.AgentSelected += OnAgentSelected;

        Add(_agentList, _agentDetail, _statusBar);

        // Key bindings
        KeyPress += OnKeyPress;

        // Start polling
        Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(2), (_) =>
        {
            Task.Run(async () => await RefreshStateAsync());
            return true; // keep polling
        });

        // Initial load
        Task.Run(async () => await RefreshStateAsync());
    }

    private void OnKeyPress(View.KeyEventEventArgs args)
    {
        if (args.KeyEvent.Key == Key.q || args.KeyEvent.Key == Key.Q)
        {
            Application.RequestStop();
            args.Handled = true;
        }
    }

    private void OnAgentSelected(string agentId)
    {
        _selectedAgentId = agentId;
        UpdateDetailView();
    }

    private async Task RefreshStateAsync()
    {
        var state = await _client.GetStateAsync();
        if (state is null)
        {
            _statusBar.SetConnectionStatus(false);
            return;
        }

        _lastState = state;
        _statusBar.SetConnectionStatus(true);

        var agents = state.Instances.Select(i => new AgentListItem(
            i.Id,
            i.ProjectName ?? i.SessionId ?? "unknown",
            GetStateIcon(i.State)
        )).ToList();

        Application.MainLoop.Invoke(() =>
        {
            _agentList.UpdateAgents(agents);
            _statusBar.Update(
                pttActive: false,
                selectedId: state.SelectedInstanceId,
                agentCount: state.Instances.Count,
                maxAgents: 9,
                audioLevel: 0
            );

            if (_selectedAgentId is null && state.Instances.Count > 0)
            {
                _selectedAgentId = state.Instances[0].Id;
            }

            UpdateDetailView();
        });
    }

    private void UpdateDetailView()
    {
        if (_lastState is null || _selectedAgentId is null)
            return;

        var agent = _lastState.Instances.FirstOrDefault(i => i.Id == _selectedAgentId);
        if (agent is null)
            return;

        _agentDetail.UpdateTitle($" Agent: {agent.ProjectName ?? "unknown"} (#{agent.Id}) ");
        _agentDetail.UpdateAgent(
            status: agent.State,
            project: agent.Cwd,
            voice: agent.VoiceProfile ?? "(auto)",
            session: agent.SessionId?[..Math.Min(agent.SessionId.Length, 12)]
        );
    }

    private static string GetStateIcon(string state) => state switch
    {
        "Busy" => "● Busy",
        "Idle" => "○ Idle",
        "WaitingForInput" => "◐ Wait",
        "Disconnected" => "✕ Disc",
        _ => "? " + state
    };
}
