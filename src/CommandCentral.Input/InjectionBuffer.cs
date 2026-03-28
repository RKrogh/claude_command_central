using System.Collections.Concurrent;
using CommandCentral.Core.Events;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;

namespace CommandCentral.Input;

public sealed class InjectionBuffer(
    IEventBus eventBus,
    ILogger<InjectionBuffer> logger)
{
    private readonly ConcurrentDictionary<string, string> _pending = new();

    public void Buffer(string instanceId, string text)
    {
        _pending[instanceId] = text;
        eventBus.Publish(new DaemonEvent(DaemonEventType.TextBuffered, instanceId,
            $"Buffered: \"{(text.Length > 50 ? text[..50] + "..." : text)}\""));
        logger.LogInformation("Text buffered for instance {Id} ({Length} chars)", instanceId, text.Length);
    }

    public string? TryConsume(string instanceId)
    {
        if (_pending.TryRemove(instanceId, out var text))
        {
            eventBus.Publish(new DaemonEvent(DaemonEventType.TextInjected, instanceId));
            return text;
        }
        return null;
    }

    public bool HasPending(string instanceId) => _pending.ContainsKey(instanceId);

    public IReadOnlyCollection<string> GetPendingInstanceIds() => _pending.Keys.ToList();
}
