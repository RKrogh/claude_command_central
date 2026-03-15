using Terminal.Gui;

namespace CommandCentral.Tui.Views;

public sealed class AgentDetailView : FrameView
{
    private readonly Label _status;
    private readonly Label _project;
    private readonly Label _voice;
    private readonly Label _session;
    private readonly ListView _activityLog;

    public AgentDetailView()
    {
        Title = "Agent Detail";

        _status = new Label { X = 1, Y = 0, Text = "Status: --" };
        _project = new Label { X = 1, Y = 1, Text = "Project: --" };
        _voice = new Label { X = 1, Y = 2, Text = "Voice: --" };
        _session = new Label { X = 1, Y = 3, Text = "Session: --" };

        var activityLabel = new Label { X = 1, Y = 5, Text = "Recent Activity" };
        var separator = new Label { X = 1, Y = 6, Text = "─────────────────" };

        _activityLog = new ListView
        {
            X = 1,
            Y = 7,
            Width = Dim.Fill(1),
            Height = Dim.Fill()
        };

        Add(_status, _project, _voice, _session, activityLabel, separator, _activityLog);
    }

    public void UpdateAgent(string? status, string? project, string? voice, string? session)
    {
        _status.Text = $"Status: {status ?? "--"}";
        _project.Text = $"Project: {project ?? "--"}";
        _voice.Text = $"Voice: {voice ?? "--"}";
        _session.Text = $"Session: {session ?? "--"}";
    }

    public void UpdateTitle(string title)
    {
        Title = title;
    }

    public void UpdateActivityLog(IReadOnlyList<string> entries)
    {
        _activityLog.SetSource(entries.ToList());
    }
}
