namespace CommandCentral.Core.Models;

/// <summary>
/// How an instance's terminal window handle was identified.
/// Later sources are generally stronger signals than earlier ones.
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

    /// <summary>Foreground window claimed when focus targeted an instance with no binding.</summary>
    FocusClaim,

    /// <summary>Explicit rebind hotkey (leader, then rebind key).</summary>
    Manual
}
