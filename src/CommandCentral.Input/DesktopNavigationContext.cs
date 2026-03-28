using CommandCentral.Input.Platform;

namespace CommandCentral.Input;

/// <summary>
/// Tracks desktop context history for quick-back navigation.
/// </summary>
public sealed class DesktopNavigationContext
{
    private const int MaxDepth = 3;
    private readonly Stack<DesktopContext> _history = new();

    public bool HasHistory => _history.Count > 0;

    public void Push(DesktopContext context)
    {
        if (context.WindowHandle == nint.Zero)
            return;

        _history.Push(context);

        // Cap depth
        if (_history.Count > MaxDepth)
        {
            var items = _history.ToArray();
            _history.Clear();
            for (var i = MaxDepth - 1; i >= 0; i--)
                _history.Push(items[i]);
        }
    }

    public DesktopContext? Pop()
    {
        if (_history.Count == 0)
            return null;

        return _history.Pop();
    }
}
