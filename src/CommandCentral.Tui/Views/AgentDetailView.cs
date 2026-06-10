using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class AgentDetailView : FrameView
{
    private readonly Label _status;
    private readonly Label _project;
    private readonly Label _voice;
    private readonly Label _session;
    private readonly Label _window;
    private readonly Label _desktop;
    private readonly ListView _activityLog;

    public AgentDetailView()
    {
        Title = "Agent Detail";

        _status = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Text = "Status: --" };
        _project = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = "Project: --" };
        _voice = new Label { X = 1, Y = 2, Width = Dim.Fill(1), Text = "Voice: --" };
        _session = new Label { X = 1, Y = 3, Width = Dim.Fill(1), Text = "Session: --" };
        _window = new Label { X = 1, Y = 4, Width = Dim.Fill(1), Text = "Window: --" };
        _desktop = new Label { X = 1, Y = 5, Width = Dim.Fill(1), Text = "Desktop: --" };

        var activityLabel = new Label { X = 1, Y = 7, Text = "Recent Activity" };
        var separator = new Label { X = 1, Y = 8, Text = "─────────────────" };

        _activityLog = new ListView
        {
            X = 1,
            Y = 9,
            Width = Dim.Fill(1),
            Height = Dim.Fill()
        };

        Add(_status, _project, _voice, _session, _window, _desktop, activityLabel, separator, _activityLog);
    }

    public void UpdateAgent(string? status, string? project, string? voice, string? session, string? window, string? desktop)
    {
        _status.Text = $"Status: {status ?? "--"}";
        _project.Text = $"Project: {project ?? "--"}";
        _voice.Text = $"Voice: {voice ?? "--"}";
        _session.Text = $"Session: {session ?? "--"}";
        _window.Text = $"Window: {window ?? "--"}";
        _desktop.Text = $"Desktop: {desktop ?? "--"}";
    }

    public void UpdateTitle(string title)
    {
        Title = title;
    }

    public void UpdateActivityLog(IReadOnlyList<string> entries)
    {
        _activityLog.SetSource(entries.ToList());
    }

    public void ShowEmpty()
    {
        Title = "Agent Detail";
        UpdateAgent(null, null, null, null, null, null);
        UpdateActivityLog([]);
    }
}
