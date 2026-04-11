namespace CommandCentral.Input;

/// <summary>
/// State machine for the leader-key hotkey system.
/// </summary>
public enum HotkeyState
{
    /// <summary>Normal operation — only the leader key combo is intercepted.</summary>
    Idle,

    /// <summary>Leader key pressed — awaiting action key within timeout window.</summary>
    LeaderActive,

    /// <summary>PTT recording in progress — hold to record, release to stop.</summary>
    PttActive,
}
