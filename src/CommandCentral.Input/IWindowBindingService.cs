using CommandCentral.Core.Models;

namespace CommandCentral.Input;

/// <summary>
/// Binds Claude Code instances to their terminal windows.
///
/// The terminal title marker is unreliable (Claude Code overwrites the title
/// before the daemon can match), so the primary mechanism is foreground-claim
/// binding: whenever we get a strong signal that the user is sitting in an
/// instance's terminal (prompt submit, PTT on an unbound instance, explicit
/// rebind), the current foreground window is claimed for that instance.
/// </summary>
public interface IWindowBindingService
{
    /// <summary>
    /// Resolves the terminal window at session start: title marker first
    /// (best effort), then foreground-claim fallback. Updates the instance
    /// and returns the bound handle (or <see cref="nint.Zero"/>).
    /// </summary>
    Task<nint> BindOnSessionStartAsync(InstanceInfo instance, string? windowMarker, CancellationToken ct = default);

    /// <summary>
    /// Binds the current foreground window to the instance, replacing any
    /// existing automatic binding. A <see cref="WindowBindingSource.Manual"/>
    /// binding is sticky: automatic sources never overwrite it (there is no
    /// reliable way to check whether the manually bound handle is still valid,
    /// so it persists until the user rebinds manually or the instance
    /// unregisters). Returns false if the claim was skipped or no foreground
    /// window is available (the existing binding is then left untouched).
    /// </summary>
    Task<bool> ClaimForegroundAsync(InstanceInfo instance, WindowBindingSource source, CancellationToken ct = default);
}
