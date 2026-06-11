using CommandCentral.Core.Models;

namespace CommandCentral.Core.Services;

/// <summary>
/// Persists user-tuned daemon state (voice assignments, selected instance) across restarts.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Returns a snapshot of the current state. Mutating the returned object has no effect;
    /// use <see cref="Update"/> to change and persist state.
    /// </summary>
    DaemonState State { get; }

    /// <summary>
    /// Applies a mutation to the state and persists it immediately.
    /// </summary>
    void Update(Action<DaemonState> mutate);
}
