namespace CommandCentral.Core.Api;

/// <summary>
/// Wire contracts shared between the daemon API and the TUI client.
/// Serialized with web defaults (camelCase) over GET /api/state and the
/// WebSocket event stream at /api/events.
/// </summary>
public static class EventStreamMessageKind
{
    public const string Snapshot = "snapshot";
    public const string Instance = "instance";
    public const string Daemon = "daemon";
}

/// <summary>
/// Envelope for messages on the /api/events WebSocket stream.
/// Exactly one of <see cref="Snapshot"/>, <see cref="Instance"/>, or
/// <see cref="Daemon"/> is set, indicated by <see cref="Kind"/>.
/// </summary>
public sealed record EventStreamMessage
{
    public required string Kind { get; init; }
    public StateSnapshotDto? Snapshot { get; init; }
    public InstanceEventDto? Instance { get; init; }
    public DaemonEventDto? Daemon { get; init; }
}

public sealed record StateSnapshotDto
{
    public string? SelectedInstanceId { get; init; }
    public IReadOnlyList<InstanceSnapshotDto> Instances { get; init; } = [];
}

public sealed record InstanceSnapshotDto
{
    public required string Id { get; init; }
    public string? SessionId { get; init; }
    public string? Cwd { get; init; }
    public string? ProjectName { get; init; }
    public required string State { get; init; }
    public string? VoiceProfile { get; init; }
    public DateTime LastActivity { get; init; }
    public bool WindowBound { get; init; }
    public Guid? DesktopId { get; init; }
    public IReadOnlyList<ActivityEntryDto> RecentActivity { get; init; } = [];
}

public sealed record ActivityEntryDto(DateTime Timestamp, string Message);

/// <summary>
/// Instance lifecycle event. <see cref="Instance"/> carries the current
/// snapshot of the instance (when it still exists) so clients can upsert
/// without a follow-up state fetch.
/// </summary>
public sealed record InstanceEventDto(
    string Type,
    string InstanceId,
    string? State,
    string? Message,
    DateTime Timestamp,
    InstanceSnapshotDto? Instance);

public sealed record DaemonEventDto(
    string Type,
    string? InstanceId,
    string? Message,
    DateTime Timestamp);
