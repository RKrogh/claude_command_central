using CommandCentral.Core.Api;

namespace CommandCentral.Tui.Services;

/// <summary>
/// Pure display formatting for agent data — kept out of the views so it can
/// be unit tested without Terminal.Gui.
/// </summary>
public static class AgentFormatter
{
    public static string StateIcon(string state) => state switch
    {
        "Busy" => "● Busy",
        "Idle" => "○ Idle",
        "WaitingForInput" => "◐ Wait",
        "Disconnected" => "✕ Disc",
        _ => "? " + state
    };

    public static string FormatListItem(InstanceSnapshotDto instance)
    {
        var name = instance.ProjectName ?? instance.SessionId ?? "unknown";
        if (name.Length > 14)
            name = name[..13] + "…";

        var window = instance.WindowBound ? "W✓" : "W✗";
        var desktop = FormatDesktop(instance.DesktopId);

        return $"[{instance.Id}] {name,-14} {StateIcon(instance.State),-6} {window} {desktop}";
    }

    public static string FormatDesktop(Guid? desktopId) =>
        desktopId is { } id ? $"D:{id.ToString("N")[..4]}" : "D:--";

    public static string FormatActivityEntry(ActivityEntryDto entry) =>
        $"{entry.Timestamp.ToLocalTime():HH:mm:ss} {entry.Message}";

    /// <summary>Circled digit for slots 1-9, "#id" beyond that.</summary>
    public static string SlotGlyph(string id) =>
        id.Length == 1 && id[0] is >= '1' and <= '9'
            ? ((char)('\u2776' + (id[0] - '1'))).ToString()
            : $"#{id}";

    /// <summary>Compact relative age: now, 42s, 5m, 3h, 2d.</summary>
    public static string RelativeAge(DateTime? utcTimestamp, DateTime utcNow)
    {
        if (utcTimestamp is null)
            return "--";

        var age = utcNow - utcTimestamp.Value;
        return age switch
        {
            { TotalSeconds: < 5 } => "now",
            { TotalSeconds: < 60 } => $"{(int)age.TotalSeconds}s",
            { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes}m",
            { TotalHours: < 24 } => $"{(int)age.TotalHours}h",
            _ => $"{(int)age.TotalDays}d",
        };
    }
}
