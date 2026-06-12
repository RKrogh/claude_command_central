using Terminal.Gui;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Views;

public sealed class MainWindow : Toplevel
{
    private readonly TuiStateStore _store;
    private readonly HeaderView _header;
    private readonly AgentListView _agentList;
    private readonly AgentDetailView _agentDetail;
    private readonly SettingsView _settings;
    private readonly StatusBarView _statusBar;
    private string? _selectedAgentId;
    private bool _showSettings;
    private int _spinnerFrame;

    public MainWindow(TuiStateStore store, DaemonClient client, string daemonUrl)
    {
        _store = store;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        ColorScheme = Theme.Canvas;

        _header = new HeaderView(daemonUrl)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        _agentList = new AgentListView
        {
            X = 0,
            Y = 1,
            Width = Dim.Percent(42),
            Height = Dim.Fill(1)
        };

        _agentDetail = new AgentDetailView
        {
            X = Pos.Right(_agentList),
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        _settings = new SettingsView(daemonUrl)
        {
            X = Pos.Right(_agentList),
            Y = 1,
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

        // Settings edits go straight to the daemon; the returned effective
        // config refreshes the store (and thereby every view).
        _settings.ApplyEdit = async update =>
        {
            var (config, error) = await client.UpdateConfigAsync(update);
            if (config is not null)
                _store.SetConfig(config);
            return error;
        };

        Add(_header, _agentList, _agentDetail, _settings, _statusBar);

        KeyPress += OnKeyPress;

        // Store mutations happen on background threads (WebSocket reader);
        // marshal rendering onto the UI loop.
        _store.Changed += () => Application.MainLoop?.Invoke(Render);

        // Spinner animation. Cheap when nothing is busy: the views skip
        // repainting unless they actually animate.
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(120), (_) =>
        {
            _spinnerFrame++;
            _agentList.Tick(_spinnerFrame);
            if (!_store.Connected)
                _header.Tick(_spinnerFrame);
            return true;
        });

        // Clock and relative ages.
        Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(1), (_) =>
        {
            _header.Tick(_spinnerFrame);
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
                if (_showSettings)
                    _settings.SetFocus();
                else
                    _agentList.SetFocus();
                SetNeedsDisplay();
                args.Handled = true;
                break;
        }
    }

    private void OnAgentSelected(string agentId)
    {
        _selectedAgentId = agentId;
        Render();
    }

    private void Render()
    {
        var agents = _store.GetAgents();

        if (_selectedAgentId is null || agents.All(a => a.Info.Id != _selectedAgentId))
            _selectedAgentId = _store.SelectedInstanceId ?? agents.FirstOrDefault()?.Info.Id;

        _agentList.UpdateAgents(agents, _selectedAgentId, _store.SelectedInstanceId, _store.PttInstanceId);
        _agentDetail.UpdateAgent(_selectedAgentId is null ? null : _store.GetAgent(_selectedAgentId));
        _header.Update(_store.Connected, _store.PttInstanceId, _store.TtsInstanceId, _store.LeaderActive,
            _store.Config?.Hotkeys.LeaderKey);
        _settings.UpdateConfig(_store.Config);
        RenderStatusBar();
    }

    private void RenderStatusBar()
    {
        var agents = _store.GetAgents();
        _statusBar.Update(
            _store.Connected,
            _store.SelectedInstanceId,
            busy: agents.Count(a => a.Info.State == "Busy"),
            waiting: agents.Count(a => a.Info.State == "WaitingForInput"),
            idle: agents.Count(a => a.Info.State == "Idle"));
    }
}
