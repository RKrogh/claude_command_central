using CommandCentral.Core.Models;

namespace CommandCentral.Core.Events;

public enum InstanceEventType
{
    Added,
    Removed,
    StateChanged,
    ActivityLogged
}

public sealed record InstanceEvent(
    InstanceEventType Type,
    string InstanceId,
    InstanceState? State = null,
    string? Message = null,
    DateTime? Timestamp = null)
{
    public DateTime EffectiveTimestamp => Timestamp ?? DateTime.UtcNow;
}
