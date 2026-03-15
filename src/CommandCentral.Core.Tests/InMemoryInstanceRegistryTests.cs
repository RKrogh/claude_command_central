using CommandCentral.Core.Events;
using CommandCentral.Core.Models;
using CommandCentral.Core.Services;

namespace CommandCentral.Core.Tests;

public class InMemoryInstanceRegistryTests
{
    private readonly InMemoryEventBus _eventBus = new();
    private readonly InMemoryInstanceRegistry _registry;

    public InMemoryInstanceRegistryTests()
    {
        _registry = new InMemoryInstanceRegistry(_eventBus);
    }

    [Fact]
    public void Register_AssignsIncrementingIds()
    {
        var i1 = _registry.Register("session-1", "/project/a");
        var i2 = _registry.Register("session-2", "/project/b");

        Assert.Equal("1", i1.Id);
        Assert.Equal("2", i2.Id);
    }

    [Fact]
    public void Register_DeriesProjectNameFromCwd()
    {
        var instance = _registry.Register("session-1", "/home/user/Projects/my-api");

        Assert.Equal("my-api", instance.ProjectName);
    }

    [Fact]
    public void Register_SetsFirstAsSelected()
    {
        _registry.Register("session-1");

        Assert.Equal("1", _registry.SelectedInstanceId);
    }

    [Fact]
    public void Register_DoesNotOverrideExistingSelection()
    {
        _registry.Register("session-1");
        _registry.Register("session-2");

        Assert.Equal("1", _registry.SelectedInstanceId);
    }

    [Fact]
    public void GetBySessionId_ReturnsCorrectInstance()
    {
        _registry.Register("session-1", "/project/a");

        var result = _registry.GetBySessionId("session-1");

        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("/project/a", result.Cwd);
    }

    [Fact]
    public void GetBySessionId_ReturnsNullForUnknown()
    {
        var result = _registry.GetBySessionId("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetById_ReturnsCorrectInstance()
    {
        _registry.Register("session-1");

        var result = _registry.GetById("1");

        Assert.NotNull(result);
        Assert.Equal("session-1", result.SessionId);
    }

    [Fact]
    public void GetAll_ReturnsAllOrderedById()
    {
        _registry.Register("session-2");
        _registry.Register("session-1");
        _registry.Register("session-3");

        var all = _registry.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Equal("1", all[0].Id);
        Assert.Equal("2", all[1].Id);
        Assert.Equal("3", all[2].Id);
    }

    [Fact]
    public void Unregister_RemovesInstance()
    {
        _registry.Register("session-1");

        var result = _registry.Unregister("session-1");

        Assert.True(result);
        Assert.Null(_registry.GetBySessionId("session-1"));
        Assert.Null(_registry.GetById("1"));
        Assert.Empty(_registry.GetAll());
    }

    [Fact]
    public void Unregister_ReturnsFalseForUnknown()
    {
        Assert.False(_registry.Unregister("nonexistent"));
    }

    [Fact]
    public void Unregister_ReassignsSelectedIfNeeded()
    {
        _registry.Register("session-1");
        _registry.Register("session-2");

        _registry.Unregister("session-1");

        Assert.Equal("2", _registry.SelectedInstanceId);
    }

    [Fact]
    public void Unregister_ClearsSelectedWhenLastRemoved()
    {
        _registry.Register("session-1");

        _registry.Unregister("session-1");

        Assert.Null(_registry.SelectedInstanceId);
    }

    [Fact]
    public void Register_ReusesFreedIds()
    {
        _registry.Register("session-1");
        _registry.Register("session-2");
        _registry.Unregister("session-1");

        var reused = _registry.Register("session-3");

        Assert.Equal("1", reused.Id);
    }

    [Fact]
    public void UpdateState_ChangesInstanceState()
    {
        _registry.Register("session-1");

        _registry.UpdateState("session-1", InstanceState.Busy);

        var instance = _registry.GetBySessionId("session-1");
        Assert.Equal(InstanceState.Busy, instance!.State);
    }

    [Fact]
    public void UpdateState_UpdatesLastActivity()
    {
        var instance = _registry.Register("session-1");
        var before = instance.LastActivity;

        Thread.Sleep(10);
        _registry.UpdateState("session-1", InstanceState.Busy);

        Assert.True(instance.LastActivity > before);
    }

    [Fact]
    public void Register_PublishesAddedEvent()
    {
        InstanceEvent? received = null;
        _eventBus.SubscribeInstances(e => received = e);

        _registry.Register("session-1", "/project/test");

        Assert.NotNull(received);
        Assert.Equal(InstanceEventType.Added, received.Type);
        Assert.Equal("1", received.InstanceId);
    }

    [Fact]
    public void Unregister_PublishesRemovedEvent()
    {
        _registry.Register("session-1");

        InstanceEvent? received = null;
        _eventBus.SubscribeInstances(e => received = e);

        _registry.Unregister("session-1");

        Assert.NotNull(received);
        Assert.Equal(InstanceEventType.Removed, received.Type);
    }

    [Fact]
    public void UpdateState_PublishesStateChangedEvent()
    {
        _registry.Register("session-1");

        InstanceEvent? received = null;
        _eventBus.SubscribeInstances(e => received = e);

        _registry.UpdateState("session-1", InstanceState.WaitingForInput);

        Assert.NotNull(received);
        Assert.Equal(InstanceEventType.StateChanged, received.Type);
        Assert.Equal(InstanceState.WaitingForInput, received.State);
    }

    [Fact]
    public void Register_ThrowsWhenMaxReached()
    {
        var smallRegistry = new InMemoryInstanceRegistry(_eventBus, maxInstances: 2);
        smallRegistry.Register("session-1");
        smallRegistry.Register("session-2");

        Assert.Throws<InvalidOperationException>(() => smallRegistry.Register("session-3"));
    }
}
