using CommandCentral.Core.Events;
using CommandCentral.Core.Services;

namespace CommandCentral.Core.Tests;

public class InMemoryEventBusTests
{
    private readonly InMemoryEventBus _bus = new();

    [Fact]
    public void Publish_InstanceEvent_NotifiesSubscribers()
    {
        InstanceEvent? received = null;
        _bus.SubscribeInstances(e => received = e);

        var evt = new InstanceEvent(InstanceEventType.Added, "1", Message: "test");
        _bus.Publish(evt);

        Assert.Equal(evt, received);
    }

    [Fact]
    public void Publish_DaemonEvent_NotifiesSubscribers()
    {
        DaemonEvent? received = null;
        _bus.SubscribeDaemon(e => received = e);

        var evt = new DaemonEvent(DaemonEventType.PttStarted, "1");
        _bus.Publish(evt);

        Assert.Equal(evt, received);
    }

    [Fact]
    public void Dispose_Subscription_StopsNotifications()
    {
        var count = 0;
        var sub = _bus.SubscribeInstances(_ => count++);

        _bus.Publish(new InstanceEvent(InstanceEventType.Added, "1"));
        sub.Dispose();
        _bus.Publish(new InstanceEvent(InstanceEventType.Added, "2"));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_NotifiesMultipleSubscribers()
    {
        var count = 0;
        _bus.SubscribeInstances(_ => count++);
        _bus.SubscribeInstances(_ => count++);

        _bus.Publish(new InstanceEvent(InstanceEventType.Added, "1"));

        Assert.Equal(2, count);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var evt = new InstanceEvent(InstanceEventType.Added, "1");

        var exception = Record.Exception(() => _bus.Publish(evt));

        Assert.Null(exception);
    }
}
