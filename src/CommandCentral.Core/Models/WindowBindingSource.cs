namespace CommandCentral.Core.Models;

/// <summary>
/// How an instance's terminal window handle was identified.
/// Precedence: <see cref="Manual"/> beats all automatic sources and is only
/// replaced by another manual rebind (or instance unregistration). Among the
/// automatic sources (<see cref="TitleMarker"/>, <see cref="SessionStartForeground"/>,
/// <see cref="PromptSubmit"/>, <see cref="PttClaim"/>), <see cref="PromptSubmit"/>
/// is the freshest signal and freely overwrites the others on every prompt.
/// </summary>
public enum WindowBindingSource
{
    /// <summary>No window bound yet.</summary>
    None,

    /// <summary>Terminal title marker (cc:&lt;hex&gt;) matched at session start. Best effort —
    /// Claude Code often resets the title before the daemon can match.</summary>
    TitleMarker,

    /// <summary>Foreground window claimed when the SessionStart hook arrived.</summary>
    SessionStartForeground,

    /// <summary>Foreground window claimed when the user submitted a prompt in this instance.
    /// Strongest automatic signal: the user just typed in that terminal.</summary>
    PromptSubmit,

    /// <summary>Foreground window claimed when PTT targeted an instance with no binding.</summary>
    PttClaim,

    /// <summary>Explicit rebind hotkey (leader, then rebind key). Sticky: automatic
    /// sources never overwrite a manual binding.</summary>
    Manual
}
