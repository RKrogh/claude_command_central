using SharpHook.Data;

namespace CommandCentral.Input;

/// <summary>
/// Parsed key combination: modifier flags + key code.
/// Parsed from strings like "Ctrl+1", "Ctrl+Shift+K", "Alt+F5".
/// </summary>
public readonly record struct KeyCombo(EventMask Modifiers, KeyCode Key)
{
    /// <summary>
    /// Checks if a keyboard event matches this combo.
    /// </summary>
    public bool Matches(EventMask eventMask, KeyCode eventKey)
    {
        if (eventKey != Key)
            return false;

        // Check required modifiers are present (left or right variants accepted)
        if (Modifiers.HasFlag(EventMask.LeftCtrl) && !eventMask.HasCtrl())
            return false;
        if (Modifiers.HasFlag(EventMask.LeftShift) && !eventMask.HasShift())
            return false;
        if (Modifiers.HasFlag(EventMask.LeftAlt) && !eventMask.HasAlt())
            return false;

        // Reject extra modifiers not in this combo (Ctrl+1 must NOT match Ctrl+Shift+1)
        if (!Modifiers.HasFlag(EventMask.LeftCtrl) && eventMask.HasCtrl())
            return false;
        if (!Modifiers.HasFlag(EventMask.LeftShift) && eventMask.HasShift())
            return false;
        if (!Modifiers.HasFlag(EventMask.LeftAlt) && eventMask.HasAlt())
            return false;

        return true;
    }

    /// <summary>
    /// Parses a key combo string like "Ctrl+Shift+K" into a KeyCombo.
    /// </summary>
    public static KeyCombo Parse(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = EventMask.None;
        KeyCode? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= EventMask.LeftCtrl;
                    break;
                case "shift":
                    modifiers |= EventMask.LeftShift;
                    break;
                case "alt":
                    modifiers |= EventMask.LeftAlt;
                    break;
                default:
                    key = ParseKeyCode(part);
                    break;
            }
        }

        if (key is null)
            throw new ArgumentException($"No key found in combo '{combo}'. Expected format: 'Ctrl+K', 'Ctrl+Shift+1', etc.");

        return new KeyCombo(modifiers, key.Value);
    }

    public static bool TryParse(string combo, out KeyCombo result)
    {
        try
        {
            result = Parse(combo);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static KeyCode ParseKeyCode(string key) => key.ToLowerInvariant() switch
    {
        // Numbers
        "0" => KeyCode.Vc0,
        "1" => KeyCode.Vc1,
        "2" => KeyCode.Vc2,
        "3" => KeyCode.Vc3,
        "4" => KeyCode.Vc4,
        "5" => KeyCode.Vc5,
        "6" => KeyCode.Vc6,
        "7" => KeyCode.Vc7,
        "8" => KeyCode.Vc8,
        "9" => KeyCode.Vc9,

        // Letters
        "a" => KeyCode.VcA, "b" => KeyCode.VcB, "c" => KeyCode.VcC,
        "d" => KeyCode.VcD, "e" => KeyCode.VcE, "f" => KeyCode.VcF,
        "g" => KeyCode.VcG, "h" => KeyCode.VcH, "i" => KeyCode.VcI,
        "j" => KeyCode.VcJ, "k" => KeyCode.VcK, "l" => KeyCode.VcL,
        "m" => KeyCode.VcM, "n" => KeyCode.VcN, "o" => KeyCode.VcO,
        "p" => KeyCode.VcP, "q" => KeyCode.VcQ, "r" => KeyCode.VcR,
        "s" => KeyCode.VcS, "t" => KeyCode.VcT, "u" => KeyCode.VcU,
        "v" => KeyCode.VcV, "w" => KeyCode.VcW, "x" => KeyCode.VcX,
        "y" => KeyCode.VcY, "z" => KeyCode.VcZ,

        // Function keys
        "f1" => KeyCode.VcF1, "f2" => KeyCode.VcF2, "f3" => KeyCode.VcF3,
        "f4" => KeyCode.VcF4, "f5" => KeyCode.VcF5, "f6" => KeyCode.VcF6,
        "f7" => KeyCode.VcF7, "f8" => KeyCode.VcF8, "f9" => KeyCode.VcF9,
        "f10" => KeyCode.VcF10, "f11" => KeyCode.VcF11, "f12" => KeyCode.VcF12,

        // Special keys
        "space" => KeyCode.VcSpace,
        "backquote" or "backtick" or "`" or "oemtilde" => KeyCode.VcBackQuote,
        "section" or "§" or "102" or "oem102" => KeyCode.Vc102,
        "tab" => KeyCode.VcTab,
        "enter" or "return" => KeyCode.VcEnter,
        "escape" or "esc" => KeyCode.VcEscape,

        _ => throw new ArgumentException($"Unknown key: '{key}'")
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(EventMask.LeftCtrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(EventMask.LeftShift)) parts.Add("Shift");
        if (Modifiers.HasFlag(EventMask.LeftAlt)) parts.Add("Alt");
        parts.Add(Key.ToString().Replace("Vc", ""));
        return string.Join("+", parts);
    }
}
