using System.Collections.Concurrent;
using CommandCentral.Core.Events;

namespace CommandCentral.Core.Services;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Guid, Action<InstanceEvent>> _instanceSubscribers = new();
    private readonly ConcurrentDictionary<Guid, Action<DaemonEvent>> _daemonSubscribers = new();

    public void Publish(InstanceEvent instanceEvent)
    {
        foreach (var handler in _instanceSubscribers.Values)
            handler(instanceEvent);
    }

    public void Publish(DaemonEvent daemonEvent)
    {
        foreach (var handler in _daemonSubscribers.Values)
            handler(daemonEvent);
    }

    public IDisposable SubscribeInstances(Action<InstanceEvent> handler)
    {
        var id = Guid.NewGuid();
        _instanceSubscribers[id] = handler;
        return new Subscription(() => _instanceSubscribers.TryRemove(id, out _));
    }

    public IDisposable SubscribeDaemon(Action<DaemonEvent> handler)
    {
        var id = Guid.NewGuid();
        _daemonSubscribers[id] = handler;
        return new Subscription(() => _daemonSubscribers.TryRemove(id, out _));
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
