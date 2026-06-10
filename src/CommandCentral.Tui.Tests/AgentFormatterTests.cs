using CommandCentral.Core.Api;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Tests;

public class AgentFormatterTests
{
    [Theory]
    [InlineData("Busy", "● Busy")]
    [InlineData("Idle", "○ Idle")]
    [InlineData("WaitingForInput", "◐ Wait")]
    [InlineData("Disconnected", "✕ Disc")]
    [InlineData("Weird", "? Weird")]
    public void StateIcon_MapsKnownStates(string state, string expected)
    {
        Assert.Equal(expected, AgentFormatter.StateIcon(state));
    }

    [Fact]
    public void FormatListItem_IncludesNumberNameStateWindowAndDesktop()
    {
        var desktopId = Guid.Parse("1a2b3c4d-0000-0000-0000-000000000000");
        var instance = new InstanceSnapshotDto
        {
            Id = "3",
            ProjectName = "api-backend",
            State = "Busy",
            WindowBound = true,
            DesktopId = desktopId
        };

        var line = AgentFormatter.FormatListItem(instance);

        Assert.StartsWith("[3] api-backend", line);
        Assert.Contains("● Busy", line);
        Assert.Contains("W✓", line);
        Assert.Contains("D:1a2b", line);
    }

    [Fact]
    public void FormatListItem_UnboundWindowAndUnknownDesktop()
    {
        var instance = new InstanceSnapshotDto
        {
            Id = "1",
            ProjectName = "x",
            State = "Idle",
            WindowBound = false,
            DesktopId = null
        };

        var line = AgentFormatter.FormatListItem(instance);

        Assert.Contains("W✗", line);
        Assert.Contains("D:--", line);
    }

    [Fact]
    public void FormatListItem_TruncatesLongNames()
    {
        var instance = new InstanceSnapshotDto
        {
            Id = "1",
            ProjectName = "a-very-long-project-name",
            State = "Idle"
        };

        var line = AgentFormatter.FormatListItem(instance);

        Assert.Contains("a-very-long-p…", line);
        Assert.DoesNotContain("a-very-long-project-name", line);
    }

    [Fact]
    public void FormatListItem_FallsBackToSessionIdThenUnknown()
    {
        var withSession = new InstanceSnapshotDto { Id = "1", SessionId = "abc", State = "Idle" };
        var bare = new InstanceSnapshotDto { Id = "2", State = "Idle" };

        Assert.Contains("abc", AgentFormatter.FormatListItem(withSession));
        Assert.Contains("unknown", AgentFormatter.FormatListItem(bare));
    }

    [Fact]
    public void FormatActivityEntry_ShowsLocalTimeAndMessage()
    {
        var entry = new ActivityEntryDto(
            new DateTime(2026, 6, 10, 12, 30, 45, DateTimeKind.Utc), "Response complete");

        var line = AgentFormatter.FormatActivityEntry(entry);

        Assert.EndsWith("Response complete", line);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2} ", line);
    }
}
