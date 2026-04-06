namespace CommandCentral.Core.Models;

public sealed class InstanceInfo
{
    public required string Id { get; init; }
    public required string SessionId { get; set; }
    public string? Cwd { get; set; }
    public string? ProjectName { get; set; }
    public nint WindowHandle { get; set; }
    public string? VoiceProfile { get; set; }
    public InstanceState State { get; set; } = InstanceState.Idle;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public string? LastAssistantMessage { get; set; }
}
