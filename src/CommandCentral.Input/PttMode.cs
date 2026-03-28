namespace CommandCentral.Input;

public enum PttMode
{
    /// <summary>Normal PTT: inject on same desktop, buffer if cross-desktop.</summary>
    Normal,

    /// <summary>Focus PTT: switch to target desktop first, then inject directly.</summary>
    FocusPtt,
}
