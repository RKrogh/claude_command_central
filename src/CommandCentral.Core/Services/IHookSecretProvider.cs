namespace CommandCentral.Core.Services;

/// <summary>
/// Supplies the shared secret that hook requests must present.
/// Null means hook authentication is not enforced (disabled by config,
/// or no secret could be established).
/// </summary>
public interface IHookSecretProvider
{
    string? Secret { get; }
}
