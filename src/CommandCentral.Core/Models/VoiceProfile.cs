namespace CommandCentral.Core.Models;

public sealed record VoiceProfile(
    string Name,
    string Engine,
    bool IsAssigned = false);
