using CommandCentral.Core.Configuration;
using CommandCentral.Core.Models;
using Microsoft.Extensions.Options;

namespace CommandCentral.Output;

public sealed class VoiceAssigner(IOptions<CommandCentralOptions> options)
{
    private readonly Dictionary<string, string> _assignments = new();
    private readonly List<string> _defaultVoices =
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

    public string AssignVoice(InstanceInfo instance)
    {
        var config = options.Value.Tts.Voices;

        // Check for explicit configuration
        if (config.TryGetValue(instance.Id, out var voiceOptions))
        {
            _assignments[instance.Id] = voiceOptions.Name;
            return voiceOptions.Name;
        }

        // Auto-assign from pool
        if (options.Value.Instances.AutoAssignVoices)
        {
            var usedVoices = _assignments.Values.ToHashSet();
            var available = _defaultVoices.FirstOrDefault(v => !usedVoices.Contains(v))
                ?? _defaultVoices[0];

            _assignments[instance.Id] = available;
            return available;
        }

        return _defaultVoices[0];
    }

    public void ReleaseVoice(string instanceId)
    {
        _assignments.Remove(instanceId);
    }

    public string? GetAssignedVoice(string instanceId)
    {
        return _assignments.GetValueOrDefault(instanceId);
    }
}
