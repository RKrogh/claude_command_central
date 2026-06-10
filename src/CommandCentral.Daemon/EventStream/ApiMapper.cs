using CommandCentral.Core.Api;
using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;

namespace CommandCentral.Daemon.EventStream;

/// <summary>
/// Maps internal state and events to the wire DTOs shared with the TUI.
/// </summary>
public static class ApiMapper
{
    public static StateSnapshotDto BuildSnapshot(IInstanceRegistry registry, InstanceActivityLog activityLog) =>
        new()
        {
            SelectedInstanceId = registry.SelectedInstanceId,
            Instances = registry.GetAll().Select(i => ToDto(i, activityLog)).ToList()
        };

    public static InstanceSnapshotDto ToDto(InstanceInfo instance, InstanceActivityLog activityLog) =>
        new()
        {
            Id = instance.Id,
            SessionId = instance.SessionId,
            Cwd = instance.Cwd,
            ProjectName = instance.ProjectName,
            State = instance.State.ToString(),
            VoiceProfile = instance.VoiceProfile,
            LastActivity = instance.LastActivity,
            WindowBound = instance.WindowHandle != nint.Zero,
            Window = $"0x{instance.WindowHandle:X}",
            WindowBindingSource = instance.WindowBindingSource.ToString(),
            WindowBoundAt = instance.WindowBoundAt,
            WtSession = instance.WtSession,
            DesktopId = instance.DesktopId,
            RecentActivity = activityLog.GetEntries(instance.Id)
                .Select(e => new ActivityEntryDto(e.Timestamp, e.Message))
                .ToList()
        };

    public static InstanceEventDto ToDto(InstanceEvent instanceEvent, IInstanceRegistry registry, InstanceActivityLog activityLog)
    {
        // Attach the current instance snapshot (when it still exists) so
        // clients can upsert without a follow-up state fetch.
        var instance = registry.GetById(instanceEvent.InstanceId);

        return new InstanceEventDto(
            instanceEvent.Type.ToString(),
            instanceEvent.InstanceId,
            instanceEvent.State?.ToString(),
            instanceEvent.Message,
            instanceEvent.EffectiveTimestamp,
            instance is null ? null : ToDto(instance, activityLog));
    }

    public static DaemonEventDto ToDto(DaemonEvent daemonEvent) =>
        new(
            daemonEvent.Type.ToString(),
            daemonEvent.InstanceId,
            daemonEvent.Message,
            daemonEvent.EffectiveTimestamp);
}
