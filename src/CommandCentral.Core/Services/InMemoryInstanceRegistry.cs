using System.Collections.Concurrent;
using CommandCentral.Core.Models;

namespace CommandCentral.Core.Services;

public sealed class InMemoryInstanceRegistry(IEventBus eventBus, int maxInstances = 25) : IInstanceRegistry
{
    private readonly ConcurrentDictionary<string, InstanceInfo> _bySessionId = new();
    private readonly ConcurrentDictionary<string, InstanceInfo> _byId = new();

    public string? SelectedInstanceId { get; set; }

    public InstanceInfo? GetById(string id) =>
        _byId.GetValueOrDefault(id);

    public InstanceInfo? GetBySessionId(string sessionId) =>
        _bySessionId.GetValueOrDefault(sessionId);

    public IReadOnlyList<InstanceInfo> GetAll() =>
        _byId.Values.OrderBy(i => i.Id).ToList();

    public InstanceInfo Register(string sessionId, string? cwd = null)
    {
        var id = FindNextAvailableId();
        var projectName = cwd is not null ? Path.GetFileName(cwd) : null;

        var instance = new InstanceInfo
        {
            Id = id,
            SessionId = sessionId,
            Cwd = cwd,
            ProjectName = projectName,
            LastActivity = DateTime.UtcNow
        };

        _bySessionId[sessionId] = instance;
        _byId[id] = instance;

        if (SelectedInstanceId is null)
            SelectedInstanceId = id;

        eventBus.Publish(new Events.InstanceEvent(
            Events.InstanceEventType.Added, id, instance.State,
            $"Registered: {projectName ?? sessionId}"));

        return instance;
    }

    public bool Unregister(string sessionId)
    {
        if (!_bySessionId.TryRemove(sessionId, out var instance))
            return false;

        _byId.TryRemove(instance.Id, out _);

        if (SelectedInstanceId == instance.Id)
            SelectedInstanceId = _byId.Keys.OrderBy(k => k).FirstOrDefault();

        eventBus.Publish(new Events.InstanceEvent(
            Events.InstanceEventType.Removed, instance.Id,
            Message: $"Unregistered: {instance.ProjectName ?? sessionId}"));

        return true;
    }

    public void UpdateState(string sessionId, InstanceState state)
    {
        if (_bySessionId.TryGetValue(sessionId, out var instance))
        {
            instance.State = state;
            instance.LastActivity = DateTime.UtcNow;

            eventBus.Publish(new Events.InstanceEvent(
                Events.InstanceEventType.StateChanged, instance.Id, state,
                $"State → {state}"));
        }
    }

    private string FindNextAvailableId()
    {
        for (var i = 1; i <= maxInstances; i++)
        {
            var id = i.ToString();
            if (!_byId.ContainsKey(id))
                return id;
        }

        throw new InvalidOperationException($"Maximum of {maxInstances} instances reached.");
    }
}
