using CommandCentral.Core.Events;

namespace CommandCentral.Core.Services;

/// <summary>
/// In-process event bus. Subscribers are dispatched synchronously in
/// subscription order, so a subscriber registered earlier always observes an
/// event before one registered later. The daemon relies on this: the
/// activity log subscribes at startup, before any event-stream socket, so
/// event DTOs built by the stream already include the activity entry for the
/// event being relayed.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly Lock _lock = new();
    private readonly List<Action<InstanceEvent>> _instanceSubscribers = [];
    private readonly List<Action<DaemonEvent>> _daemonSubscribers = [];

    public void Publish(InstanceEvent instanceEvent)
    {
        foreach (var handler in Snapshot(_instanceSubscribers))
            handler(instanceEvent);
    }

    public void Publish(DaemonEvent daemonEvent)
    {
        foreach (var handler in Snapshot(_daemonSubscribers))
            handler(daemonEvent);
    }

    public IDisposable SubscribeInstances(Action<InstanceEvent> handler) =>
        Subscribe(_instanceSubscribers, handler);

    public IDisposable SubscribeDaemon(Action<DaemonEvent> handler) =>
        Subscribe(_daemonSubscribers, handler);

    private T[] Snapshot<T>(List<T> subscribers)
    {
        lock (_lock)
        {
            return [.. subscribers];
        }
    }

    private Subscription Subscribe<T>(List<Action<T>> subscribers, Action<T> handler)
    {
        lock (_lock)
        {
            subscribers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                subscribers.Remove(handler);
            }
        });
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
