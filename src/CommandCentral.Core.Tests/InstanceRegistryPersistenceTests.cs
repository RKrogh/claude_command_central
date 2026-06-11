using CommandCentral.Core.Models;
using CommandCentral.Core.Services;

namespace CommandCentral.Core.Tests;

public class InstanceRegistryPersistenceTests
{
    private sealed class InMemoryStateStore(DaemonState? initial = null) : IStateStore
    {
        private readonly DaemonState _state = initial ?? new DaemonState();

        public DaemonState State => new()
        {
            SelectedInstanceId = _state.SelectedInstanceId,
            VoiceAssignments = new Dictionary<string, string>(_state.VoiceAssignments)
        };

        public void Update(Action<DaemonState> mutate) => mutate(_state);
    }

    private readonly InMemoryEventBus _eventBus = new();

    [Fact]
    public void SettingSelectedInstance_PersistsToStateStore()
    {
        var store = new InMemoryStateStore();
        var registry = new InMemoryInstanceRegistry(_eventBus, store);
        registry.Register("session-1");
        registry.Register("session-2");

        registry.SelectedInstanceId = "2";

        Assert.Equal("2", store.State.SelectedInstanceId);
    }

    [Fact]
    public void Register_RestoresPersistedSelection_WhenPreferredSlotRegisters()
    {
        var store = new InMemoryStateStore(new DaemonState { SelectedInstanceId = "2" });
        var registry = new InMemoryInstanceRegistry(_eventBus, store);

        registry.Register("session-1"); // slot 1: auto-selected (preferred not seen yet)
        Assert.Equal("1", registry.SelectedInstanceId);

        registry.Register("session-2"); // slot 2: matches persisted preference
        Assert.Equal("2", registry.SelectedInstanceId);
    }

    [Fact]
    public void Register_DoesNotRestorePreference_AfterUserSelectedThisSession()
    {
        var store = new InMemoryStateStore(new DaemonState { SelectedInstanceId = "3" });
        var registry = new InMemoryInstanceRegistry(_eventBus, store);
        registry.Register("session-1");
        registry.Register("session-2");

        registry.SelectedInstanceId = "1"; // explicit user choice this session

        registry.Register("session-3"); // slot 3 = old preference, must not steal selection
        Assert.Equal("1", registry.SelectedInstanceId);
    }

    [Fact]
    public void AutoSelection_DoesNotOverwritePersistedPreference()
    {
        var store = new InMemoryStateStore(new DaemonState { SelectedInstanceId = "2" });
        var registry = new InMemoryInstanceRegistry(_eventBus, store);

        // Slot 1 registers and is auto-selected, but the persisted preference
        // ("2") must remain in the store untouched.
        registry.Register("session-1");

        Assert.Equal("2", store.State.SelectedInstanceId);
    }

    [Fact]
    public void Unregister_FallbackSelection_IsNotPersisted()
    {
        var store = new InMemoryStateStore();
        var registry = new InMemoryInstanceRegistry(_eventBus, store);
        registry.Register("session-1");
        registry.Register("session-2");
        registry.SelectedInstanceId = "2";

        registry.Unregister("session-2"); // falls back to slot 1 in memory

        Assert.Equal("1", registry.SelectedInstanceId);
        Assert.Equal("2", store.State.SelectedInstanceId); // preference survives
    }

    [Fact]
    public void Registry_WorksWithoutStateStore()
    {
        var registry = new InMemoryInstanceRegistry(_eventBus);

        registry.Register("session-1");
        registry.SelectedInstanceId = "1";

        Assert.Equal("1", registry.SelectedInstanceId);
    }
}
