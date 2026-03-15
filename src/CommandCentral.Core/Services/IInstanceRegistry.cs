using CommandCentral.Core.Models;

namespace CommandCentral.Core.Services;

public interface IInstanceRegistry
{
    InstanceInfo? GetById(string id);
    InstanceInfo? GetBySessionId(string sessionId);
    IReadOnlyList<InstanceInfo> GetAll();
    InstanceInfo Register(string sessionId, string? cwd = null);
    bool Unregister(string sessionId);
    void UpdateState(string sessionId, InstanceState state);
    string? SelectedInstanceId { get; set; }
}
