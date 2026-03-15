using CommandCentral.Core.Events;

namespace CommandCentral.Core.Services;

/// <summary>
/// In-process event bus for daemon ↔ TUI communication.
/// The daemon publishes events, TUI subscribes via WebSocket relay.
/// </summary>
public interface IEventBus
{
    void Publish(InstanceEvent instanceEvent);
    void Publish(DaemonEvent daemonEvent);
    IDisposable SubscribeInstances(Action<InstanceEvent> handler);
    IDisposable SubscribeDaemon(Action<DaemonEvent> handler);
}
