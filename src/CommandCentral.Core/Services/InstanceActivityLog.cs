using CommandCentral.Core.Events;

namespace CommandCentral.Core.Services;

public sealed record ActivityEntry(DateTime Timestamp, string Message);

/// <summary>
/// Bounded per-instance activity log fed by the event bus.
/// Keeps the most recent entries so a TUI connecting later still sees history.
/// </summary>
public sealed class InstanceActivityLog : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<ActivityEntry>> _entries = [];
    private readonly int _maxEntriesPerInstance;
    private readonly IDisposable _instanceSubscription;
    private readonly IDisposable _daemonSubscription;

    public InstanceActivityLog(IEventBus eventBus, int maxEntriesPerInstance = 100)
    {
        _maxEntriesPerInstance = maxEntriesPerInstance;
        _instanceSubscription = eventBus.SubscribeInstances(OnInstanceEvent);
        _daemonSubscription = eventBus.SubscribeDaemon(OnDaemonEvent);
    }

    public IReadOnlyList<ActivityEntry> GetEntries(string instanceId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(instanceId, out var queue)
                ? queue.ToList()
                : [];
        }
    }

    private void OnInstanceEvent(InstanceEvent instanceEvent)
    {
        if (instanceEvent.Type == InstanceEventType.Removed)
        {
            // Slot ids are reused — drop history so a future instance in the
            // same slot doesn't inherit it.
            lock (_lock)
            {
                _entries.Remove(instanceEvent.InstanceId);
            }
            return;
        }

        Append(
            instanceEvent.InstanceId,
            instanceEvent.EffectiveTimestamp,
            instanceEvent.Message ?? instanceEvent.Type.ToString());
    }

    private void OnDaemonEvent(DaemonEvent daemonEvent)
    {
        if (daemonEvent.InstanceId is null)
            return;

        var message = daemonEvent.Message is null
            ? daemonEvent.Type.ToString()
            : $"{daemonEvent.Type}: {daemonEvent.Message}";

        Append(daemonEvent.InstanceId, daemonEvent.EffectiveTimestamp, message);
    }

    private void Append(string instanceId, DateTime timestamp, string message)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(instanceId, out var queue))
            {
                queue = new Queue<ActivityEntry>();
                _entries[instanceId] = queue;
            }

            queue.Enqueue(new ActivityEntry(timestamp, message));
            while (queue.Count > _maxEntriesPerInstance)
                queue.Dequeue();
        }
    }

    public void Dispose()
    {
        _instanceSubscription.Dispose();
        _daemonSubscription.Dispose();
    }
}
