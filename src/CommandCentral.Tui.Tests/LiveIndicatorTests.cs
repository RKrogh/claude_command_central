using CommandCentral.Core.Api;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Tests;

public class LiveIndicatorTests
{
    private readonly TuiStateStore _store = new();

    private void Seed()
    {
        _store.ApplySnapshot(new StateSnapshotDto
        {
            SelectedInstanceId = "1",
            Instances = [new InstanceSnapshotDto { Id = "1", State = "Idle" }]
        });
    }

    private static EventStreamMessage Daemon(string type, string? instanceId = null) => new()
    {
        Kind = EventStreamMessageKind.Daemon,
        Daemon = new DaemonEventDto(type, instanceId, null, DateTime.UtcNow)
    };

    [Fact]
    public void PttStartedAndStopped_TracksRecordingInstance()
    {
        Seed();

        _store.Apply(Daemon("PttStarted", "1"));
        Assert.Equal("1", _store.PttInstanceId);

        _store.Apply(Daemon("PttStopped", "1"));
        Assert.Null(_store.PttInstanceId);
    }

    [Fact]
    public void TtsStartedAndStopped_TracksSpeakingInstance()
    {
        Seed();

        _store.Apply(Daemon("TtsStarted", "1"));
        Assert.Equal("1", _store.TtsInstanceId);

        _store.Apply(Daemon("TtsStopped", "1"));
        Assert.Null(_store.TtsInstanceId);
    }

    [Fact]
    public void LeaderEvents_ToggleLeaderActive_AndRaiseChanged()
    {
        Seed();
        var changes = 0;
        _store.Changed += () => changes++;

        _store.Apply(Daemon("LeaderActivated"));
        Assert.True(_store.LeaderActive);

        _store.Apply(Daemon("LeaderDeactivated"));
        Assert.False(_store.LeaderActive);

        // Leader events carry no instance id but must still notify the UI
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Snapshot_DoesNotResetLiveIndicators()
    {
        Seed();
        _store.Apply(Daemon("PttStarted", "1"));

        Seed();

        // A reconnect snapshot replaces agents, but in-flight PTT state is
        // owned by subsequent daemon events; it must not throw or corrupt.
        Assert.Equal("1", _store.PttInstanceId);
    }
}

public class FormatterAdditionsTests
{
    [Theory]
    [InlineData("1", "❶")]
    [InlineData("5", "❺")]
    [InlineData("9", "❾")]
    [InlineData("10", "#10")]
    [InlineData("0", "#0")]
    public void SlotGlyph_MapsDigitsToCircledGlyphs(string id, string expected)
    {
        Assert.Equal(expected, AgentFormatter.SlotGlyph(id));
    }

    [Fact]
    public void RelativeAge_FormatsCompactBuckets()
    {
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal("--", AgentFormatter.RelativeAge(null, now));
        Assert.Equal("now", AgentFormatter.RelativeAge(now.AddSeconds(-2), now));
        Assert.Equal("42s", AgentFormatter.RelativeAge(now.AddSeconds(-42), now));
        Assert.Equal("5m", AgentFormatter.RelativeAge(now.AddMinutes(-5), now));
        Assert.Equal("3h", AgentFormatter.RelativeAge(now.AddHours(-3), now));
        Assert.Equal("2d", AgentFormatter.RelativeAge(now.AddDays(-2), now));
    }
}
