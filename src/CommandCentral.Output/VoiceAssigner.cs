using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using Microsoft.Extensions.Options;

namespace CommandCentral.Output;

/// <summary>
/// Assigns a voice per slot. Precedence: explicit config (Tts:Voices) →
/// persisted assignment from a previous run → auto-assignment from the pool.
/// Assignments are persisted via <see cref="IStateStore"/> so a slot keeps
/// its voice across daemon restarts.
/// </summary>
public sealed class VoiceAssigner(IOptions<CommandCentralOptions> options, IStateStore stateStore)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _assignments = new(stateStore.State.VoiceAssignments);
    private static readonly string[] DefaultVoices =
    [
        "en_US-lessac-medium",
        "en_US-amy-medium",
        "en_US-arctic-medium",
        "en_US-danny-low",
        "en_US-joe-medium",
        "en_US-kathleen-low",
        "en_US-kusal-medium",
        "en_US-libritts_r-medium",
        "en_US-ryan-medium"
    ];

    public string AssignVoice(string instanceId)
    {
        lock (_lock)
        {
            // Explicit configuration always wins
            if (options.Value.Tts.Voices.TryGetValue(instanceId, out var voiceOptions))
            {
                SetAssignment(instanceId, voiceOptions.Name);
                return voiceOptions.Name;
            }

            // Reuse persisted/previous assignment
            if (_assignments.TryGetValue(instanceId, out var existing))
                return existing;

            // Auto-assign from pool
            if (options.Value.Instances.AutoAssignVoices)
            {
                var usedVoices = _assignments.Values.ToHashSet();
                var available = DefaultVoices.FirstOrDefault(v => !usedVoices.Contains(v))
                    ?? DefaultVoices[0];

                SetAssignment(instanceId, available);
                return available;
            }

            return DefaultVoices[0];
        }
    }

    public void ReleaseVoice(string instanceId)
    {
        lock (_lock)
        {
            if (_assignments.Remove(instanceId))
                Persist();
        }
    }

    public string? GetAssignedVoice(string instanceId)
    {
        lock (_lock)
        {
            return _assignments.GetValueOrDefault(instanceId);
        }
    }

    private void SetAssignment(string instanceId, string voice)
    {
        if (_assignments.TryGetValue(instanceId, out var current) && current == voice)
            return;

        _assignments[instanceId] = voice;
        Persist();
    }

    private void Persist() =>
        stateStore.Update(s => s.VoiceAssignments = new Dictionary<string, string>(_assignments));
}
