using CommandCentral.Core.Api;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Tests;

public class TuiStateStoreTests
{
    private static InstanceSnapshotDto Instance(
        string id,
        string state = "Idle",
        string? projectName = null,
        IReadOnlyList<ActivityEntryDto>? activity = null) =>
        new()
        {
            Id = id,
            SessionId = $"session-{id}",
            ProjectName = projectName ?? $"project-{id}",
            State = state,
            RecentActivity = activity ?? []
        };

    private static EventStreamMessage SnapshotMessage(params InstanceSnapshotDto[] instances) =>
        new()
        {
            Kind = EventStreamMessageKind.Snapshot,
            Snapshot = new StateSnapshotDto
            {
                SelectedInstanceId = instances.FirstOrDefault()?.Id,
                Instances = instances
            }
        };

    [Fact]
    public void ApplySnapshot_ReplacesAllAgents()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("1"), Instance("2")));
        store.Apply(SnapshotMessage(Instance("3")));

        var agents = store.GetAgents();
        Assert.Single(agents);
        Assert.Equal("3", agents[0].Info.Id);
    }

    [Fact]
    public void ApplySnapshot_SetsSelectedInstanceId()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("2")));

        Assert.Equal("2", store.SelectedInstanceId);
    }

    [Fact]
    public void InstanceEvent_Added_UpsertsAgent()
    {
        var store = new TuiStateStore();
        store.Apply(new EventStreamMessage
        {
            Kind = EventStreamMessageKind.Instance,
            Instance = new InstanceEventDto(
                "Added", "1", "Idle", "Registered", DateTime.UtcNow, Instance("1", projectName: "alpha"))
        });

        var agent = store.GetAgent("1");
        Assert.NotNull(agent);
        Assert.Equal("alpha", agent.Info.ProjectName);
    }

    [Fact]
    public void InstanceEvent_StateChanged_UpdatesExistingAgent()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("1", state: "Idle")));

        store.Apply(new EventStreamMessage
        {
            Kind = EventStreamMessageKind.Instance,
            Instance = new InstanceEventDto(
                "StateChanged", "1", "Busy", "State → Busy", DateTime.UtcNow, Instance("1", state: "Busy"))
        });

        Assert.Equal("Busy", store.GetAgent("1")!.Info.State);
    }

    [Fact]
    public void InstanceEvent_Removed_RemovesAgent()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("1")));

        store.Apply(new EventStreamMessage
        {
            Kind = EventStreamMessageKind.Instance,
            Instance = new InstanceEventDto("Removed", "1", null, "Unregistered", DateTime.UtcNow, null)
        });

        Assert.Null(store.GetAgent("1"));
        Assert.Empty(store.GetAgents());
    }

    [Fact]
    public void InstanceEvent_CarriesActivity_ReplacesLog()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("1")));

        var activity = new List<ActivityEntryDto>
        {
            new(DateTime.UtcNow, "Registered"),
            new(DateTime.UtcNow, "Prompt: hello")
        };

        store.Apply(new EventStreamMessage
        {
            Kind = EventStreamMessageKind.Instance,
            Instance = new InstanceEventDto(
                "ActivityLogged", "1", null, "Prompt: hello", DateTime.UtcNow,
                Instance("1", activity: activity))
        });

        var agent = store.GetAgent("1")!;
        Assert.Equal(2, agent.Activity.Count);
        Assert.Equal("Prompt: hello", agent.Activity[1].Message);
    }

    [Fact]
    public void DaemonEvent_WithInstanceId_AppendsActivity()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("1")));

        store.Apply(new EventStreamMessage
        {
            Kind = EventStreamMessageKind.Daemon,
            Daemon = new DaemonEventDto("SttResult", "1", "fix the bug", DateTime.UtcNow)
        });

        var entry = Assert.Single(store.GetAgent("1")!.Activity);
        Assert.Equal("SttResult: fix the bug", entry.Message);
    }

    [Fact]
    public void DaemonEvent_ForUnknownAgent_IsIgnored()
    {
        var store = new TuiStateStore();

        store.Apply(new EventStreamMessage
        {
            Kind = EventStreamMessageKind.Daemon,
            Daemon = new DaemonEventDto("SttResult", "9", "hello", DateTime.UtcNow)
        });

        Assert.Empty(store.GetAgents());
    }

    [Fact]
    public void DaemonEvent_SelectedInstanceChanged_UpdatesSelection()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("1"), Instance("2")));

        store.Apply(new EventStreamMessage
        {
            Kind = EventStreamMessageKind.Daemon,
            Daemon = new DaemonEventDto("SelectedInstanceChanged", "2", null, DateTime.UtcNow)
        });

        Assert.Equal("2", store.SelectedInstanceId);
    }

    [Fact]
    public void ActivityLog_IsBounded()
    {
        var store = new TuiStateStore(maxActivityEntries: 3);
        store.Apply(SnapshotMessage(Instance("1")));

        for (var i = 1; i <= 5; i++)
        {
            store.Apply(new EventStreamMessage
            {
                Kind = EventStreamMessageKind.Daemon,
                Daemon = new DaemonEventDto("TextInjected", "1", $"entry {i}", DateTime.UtcNow)
            });
        }

        var activity = store.GetAgent("1")!.Activity;
        Assert.Equal(3, activity.Count);
        Assert.Equal("TextInjected: entry 3", activity[0].Message);
        Assert.Equal("TextInjected: entry 5", activity[2].Message);
    }

    [Fact]
    public void GetAgents_OrdersByNumericId()
    {
        var store = new TuiStateStore();
        store.Apply(SnapshotMessage(Instance("10"), Instance("2"), Instance("1")));

        Assert.Equal(["1", "2", "10"], store.GetAgents().Select(a => a.Info.Id).ToArray());
    }

    [Fact]
    public void SetConnected_RaisesChangedOnlyOnTransition()
    {
        var store = new TuiStateStore();
        var changes = 0;
        store.Changed += () => changes++;

        store.SetConnected(true);
        store.SetConnected(true);
        store.SetConnected(false);

        Assert.Equal(2, changes);
        Assert.False(store.Connected);
    }

    [Fact]
    public void Apply_RaisesChanged()
    {
        var store = new TuiStateStore();
        var changes = 0;
        store.Changed += () => changes++;

        store.Apply(SnapshotMessage(Instance("1")));

        Assert.Equal(1, changes);
    }
}
