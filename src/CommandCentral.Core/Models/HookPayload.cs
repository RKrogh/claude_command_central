using System.Text.Json.Serialization;

namespace CommandCentral.Core.Models;

public sealed class HookPayload
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("last_assistant_message")]
    public string? LastAssistantMessage { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }
}
