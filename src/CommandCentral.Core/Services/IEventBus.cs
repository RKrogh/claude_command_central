using CommandCentral.Core.Events;

namespace CommandCentral.Core.Services;

/// <summary>
/// In-process event bus for daemon ↔ TUI communication.
/// The daemon publishes events, TUI subscribes via WebSocket relay.
/// Implementations must dispatch subscribers in subscription order: a
/// subscriber registered earlier observes each event before one registered
/// later (the event stream depends on the activity log running first).
/// </summary>
public interface IEventBus
{
    void Publish(InstanceEvent instanceEvent);
    void Publish(DaemonEvent daemonEvent);
    IDisposable SubscribeInstances(Action<InstanceEvent> handler);
    IDisposable SubscribeDaemon(Action<DaemonEvent> handler);
}
