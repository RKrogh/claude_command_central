using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;

namespace CommandCentral.Core.Tests;

public class InstanceActivityLogTests
{
    private readonly InMemoryEventBus _eventBus = new();

    [Fact]
    public void InstanceEvent_IsRecorded()
    {
        using var log = new InstanceActivityLog(_eventBus);

        _eventBus.Publish(new InstanceEvent(
            InstanceEventType.StateChanged, "1", InstanceState.Busy, "State → Busy"));

        var entries = log.GetEntries("1");
        Assert.Single(entries);
        Assert.Equal("State → Busy", entries[0].Message);
    }

    [Fact]
    public void InstanceEvent_WithoutMessage_FallsBackToTypeName()
    {
        using var log = new InstanceActivityLog(_eventBus);

        _eventBus.Publish(new InstanceEvent(InstanceEventType.StateChanged, "1"));

        Assert.Equal("StateChanged", log.GetEntries("1")[0].Message);
    }

    [Fact]
    public void DaemonEvent_WithInstanceId_IsRecorded()
    {
        using var log = new InstanceActivityLog(_eventBus);

        _eventBus.Publish(new DaemonEvent(DaemonEventType.SttResult, "2", "fix the auth bug"));

        var entries = log.GetEntries("2");
        Assert.Single(entries);
        Assert.Equal("SttResult: fix the auth bug", entries[0].Message);
    }

    [Fact]
    public void DaemonEvent_WithoutInstanceId_IsIgnored()
    {
        using var log = new InstanceActivityLog(_eventBus);

        _eventBus.Publish(new DaemonEvent(DaemonEventType.LeaderActivated));

        Assert.Empty(log.GetEntries("1"));
    }

    [Fact]
    public void Entries_AreBoundedPerInstance()
    {
        using var log = new InstanceActivityLog(_eventBus, maxEntriesPerInstance: 3);

        for (var i = 1; i <= 5; i++)
        {
            _eventBus.Publish(new InstanceEvent(
                InstanceEventType.ActivityLogged, "1", Message: $"entry {i}"));
        }

        var entries = log.GetEntries("1");
        Assert.Equal(3, entries.Count);
        Assert.Equal("entry 3", entries[0].Message);
        Assert.Equal("entry 5", entries[2].Message);
    }

    [Fact]
    public void Entries_AreKeptPerInstance()
    {
        using var log = new InstanceActivityLog(_eventBus);

        _eventBus.Publish(new InstanceEvent(InstanceEventType.ActivityLogged, "1", Message: "one"));
        _eventBus.Publish(new InstanceEvent(InstanceEventType.ActivityLogged, "2", Message: "two"));

        Assert.Equal("one", Assert.Single(log.GetEntries("1")).Message);
        Assert.Equal("two", Assert.Single(log.GetEntries("2")).Message);
    }

    [Fact]
    public void Removed_ClearsHistoryForSlot()
    {
        using var log = new InstanceActivityLog(_eventBus);

        _eventBus.Publish(new InstanceEvent(InstanceEventType.Added, "1", Message: "Registered"));
        _eventBus.Publish(new InstanceEvent(InstanceEventType.Removed, "1", Message: "Unregistered"));

        Assert.Empty(log.GetEntries("1"));
    }

    [Fact]
    public void UnknownInstance_ReturnsEmpty()
    {
        using var log = new InstanceActivityLog(_eventBus);

        Assert.Empty(log.GetEntries("nope"));
    }

    [Fact]
    public void Dispose_StopsRecording()
    {
        var log = new InstanceActivityLog(_eventBus);
        log.Dispose();

        _eventBus.Publish(new InstanceEvent(InstanceEventType.ActivityLogged, "1", Message: "late"));

        Assert.Empty(log.GetEntries("1"));
    }
}
