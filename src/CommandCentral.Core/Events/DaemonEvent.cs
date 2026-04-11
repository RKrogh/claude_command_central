namespace CommandCentral.Core.Events;

public enum DaemonEventType
{
    PttStarted,
    PttStopped,
    SttResult,
    TtsStarted,
    TtsStopped,
    SelectedInstanceChanged,
    TextBuffered,
    TextInjected,
    DesktopSwitched,
    LeaderActivated,
    LeaderDeactivated,
}

public sealed record DaemonEvent(
    DaemonEventType Type,
    string? InstanceId = null,
    string? Message = null,
    DateTime? Timestamp = null
)
{
    public DateTime EffectiveTimestamp => Timestamp ?? DateTime.UtcNow;
}
