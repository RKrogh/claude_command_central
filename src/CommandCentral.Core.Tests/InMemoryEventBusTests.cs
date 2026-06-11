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
    public void Publish_DispatchesInstanceSubscribersInSubscriptionOrder()
    {
        var order = new List<int>();
        _bus.SubscribeInstances(_ => order.Add(1));
        _bus.SubscribeInstances(_ => order.Add(2));
        _bus.SubscribeInstances(_ => order.Add(3));

        _bus.Publish(new InstanceEvent(InstanceEventType.Added, "1"));

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void Publish_DispatchesDaemonSubscribersInSubscriptionOrder()
    {
        var order = new List<int>();
        _bus.SubscribeDaemon(_ => order.Add(1));
        _bus.SubscribeDaemon(_ => order.Add(2));
        _bus.SubscribeDaemon(_ => order.Add(3));

        _bus.Publish(new DaemonEvent(DaemonEventType.PttStarted, "1"));

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void Publish_AfterMiddleSubscriberDisposed_PreservesOrderOfRest()
    {
        var order = new List<int>();
        _bus.SubscribeInstances(_ => order.Add(1));
        var middle = _bus.SubscribeInstances(_ => order.Add(2));
        _bus.SubscribeInstances(_ => order.Add(3));

        middle.Dispose();
        _bus.Publish(new InstanceEvent(InstanceEventType.Added, "1"));

        Assert.Equal([1, 3], order);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var evt = new InstanceEvent(InstanceEventType.Added, "1");

        var exception = Record.Exception(() => _bus.Publish(evt));

        Assert.Null(exception);
    }
}
