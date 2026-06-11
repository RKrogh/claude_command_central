using CommandCentral.Core.Api;

namespace CommandCentral.Tui.Services;

/// <summary>
/// A live view of an agent: the latest instance snapshot plus its
/// accumulated activity log.
/// </summary>
public sealed record AgentView(InstanceSnapshotDto Info, IReadOnlyList<ActivityEntryDto> Activity);

/// <summary>
/// Client-side state synchronized from the daemon. Applies snapshots and
/// stream events, and raises <see cref="Changed"/> after every mutation so
/// the view layer can re-render. Thread-safe; has no Terminal.Gui dependency
/// so it stays unit-testable.
/// </summary>
public sealed class TuiStateStore(int maxActivityEntries = 100)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, InstanceSnapshotDto> _agents = [];
    private readonly Dictionary<string, List<ActivityEntryDto>> _activity = [];

    public event Action? Changed;

    public bool Connected { get; private set; }
    public string? SelectedInstanceId { get; private set; }

    // Live daemon indicators, driven by the event stream. Instance ids of
    // in-flight PTT/TTS, and whether the leader-key window is open.
    public string? PttInstanceId { get; private set; }
    public string? TtsInstanceId { get; private set; }
    public bool LeaderActive { get; private set; }

    public IReadOnlyList<AgentView> GetAgents()
    {
        lock (_lock)
        {
            return _agents.Values
                .OrderBy(a => int.TryParse(a.Id, out var n) ? n : int.MaxValue)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
                .Select(a => new AgentView(a, _activity.GetValueOrDefault(a.Id, []).ToList()))
                .ToList();
        }
    }

    public AgentView? GetAgent(string id)
    {
        lock (_lock)
        {
            return _agents.TryGetValue(id, out var info)
                ? new AgentView(info, _activity.GetValueOrDefault(id, []).ToList())
                : null;
        }
    }

    public void SetConnected(bool connected)
    {
        lock (_lock)
        {
            if (Connected == connected)
                return;

            Connected = connected;
        }

        RaiseChanged();
    }

    public void Apply(EventStreamMessage message)
    {
        switch (message.Kind)
        {
            case EventStreamMessageKind.Snapshot when message.Snapshot is not null:
                ApplySnapshot(message.Snapshot);
                break;
            case EventStreamMessageKind.Instance when message.Instance is not null:
                ApplyInstanceEvent(message.Instance);
                break;
            case EventStreamMessageKind.Daemon when message.Daemon is not null:
                ApplyDaemonEvent(message.Daemon);
                break;
        }
    }

    public void ApplySnapshot(StateSnapshotDto snapshot)
    {
        lock (_lock)
        {
            _agents.Clear();
            _activity.Clear();
            SelectedInstanceId = snapshot.SelectedInstanceId;

            foreach (var instance in snapshot.Instances)
                Upsert(instance);
        }

        RaiseChanged();
    }

    private void ApplyInstanceEvent(InstanceEventDto instanceEvent)
    {
        lock (_lock)
        {
            if (instanceEvent.Type == nameof(Core.Events.InstanceEventType.Removed))
            {
                _agents.Remove(instanceEvent.InstanceId);
                _activity.Remove(instanceEvent.InstanceId);
            }
            else if (instanceEvent.Instance is not null)
            {
                // The attached snapshot (including its recent activity) is
                // authoritative — replacing wholesale avoids duplicate log
                // entries from locally appended daemon events.
                Upsert(instanceEvent.Instance);
            }
        }

        RaiseChanged();
    }

    private void ApplyDaemonEvent(DaemonEventDto daemonEvent)
    {
        if (daemonEvent.Type == nameof(Core.Events.DaemonEventType.SelectedInstanceChanged))
        {
            lock (_lock)
            {
                SelectedInstanceId = daemonEvent.InstanceId;
            }

            RaiseChanged();
            return;
        }

        if (UpdateLiveIndicators(daemonEvent))
            RaiseChanged();

        if (daemonEvent.InstanceId is null)
            return;

        lock (_lock)
        {
            if (!_agents.ContainsKey(daemonEvent.InstanceId))
                return;

            var message = daemonEvent.Message is null
                ? daemonEvent.Type
                : $"{daemonEvent.Type}: {daemonEvent.Message}";

            AppendActivity(daemonEvent.InstanceId, new ActivityEntryDto(daemonEvent.Timestamp, message));
        }

        RaiseChanged();
    }

    /// <summary>
    /// Tracks transient daemon activity (recording, speaking, leader window)
    /// so the UI can show live badges. Returns true when an indicator changed.
    /// </summary>
    private bool UpdateLiveIndicators(DaemonEventDto daemonEvent)
    {
        lock (_lock)
        {
            switch (daemonEvent.Type)
            {
                case nameof(Core.Events.DaemonEventType.PttStarted):
                    PttInstanceId = daemonEvent.InstanceId;
                    return true;
                case nameof(Core.Events.DaemonEventType.PttStopped):
                    PttInstanceId = null;
                    return true;
                case nameof(Core.Events.DaemonEventType.TtsStarted):
                    TtsInstanceId = daemonEvent.InstanceId;
                    return true;
                case nameof(Core.Events.DaemonEventType.TtsStopped):
                    TtsInstanceId = null;
                    return true;
                case nameof(Core.Events.DaemonEventType.LeaderActivated):
                    LeaderActive = true;
                    return true;
                case nameof(Core.Events.DaemonEventType.LeaderDeactivated):
                    LeaderActive = false;
                    return true;
                default:
                    return false;
            }
        }
    }

    private void Upsert(InstanceSnapshotDto instance)
    {
        _agents[instance.Id] = instance;

        var log = instance.RecentActivity.ToList();
        if (log.Count > maxActivityEntries)
            log.RemoveRange(0, log.Count - maxActivityEntries);

        _activity[instance.Id] = log;
    }

    private void AppendActivity(string instanceId, ActivityEntryDto entry)
    {
        if (!_activity.TryGetValue(instanceId, out var log))
        {
            log = [];
            _activity[instanceId] = log;
        }

        log.Add(entry);
        if (log.Count > maxActivityEntries)
            log.RemoveRange(0, log.Count - maxActivityEntries);
    }

    private void RaiseChanged() => Changed?.Invoke();
}
