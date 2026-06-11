namespace CommandCentral.Tui.Services;

/// <summary>
/// Exponential backoff for daemon reconnect attempts: doubles from the
/// initial delay up to the cap. Reset on a successful connection.
/// </summary>
public sealed class ReconnectBackoff(TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)
{
    private readonly TimeSpan _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
    private readonly TimeSpan _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
    private int _attempt;

    public TimeSpan NextDelay()
    {
        var delay = _initialDelay * Math.Pow(2, _attempt);
        if (delay >= _maxDelay)
            return _maxDelay;

        _attempt++;
        return delay;
    }

    public void Reset() => _attempt = 0;
}
