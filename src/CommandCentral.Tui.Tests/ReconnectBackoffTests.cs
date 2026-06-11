using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Tests;

public class ReconnectBackoffTests
{
    [Fact]
    public void Delays_DoubleUpToCap()
    {
        var backoff = new ReconnectBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(2), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(4), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(8), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(16), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(30), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(30), backoff.NextDelay());
    }

    [Fact]
    public void Reset_StartsOverFromInitialDelay()
    {
        var backoff = new ReconnectBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        backoff.NextDelay();
        backoff.NextDelay();
        backoff.Reset();

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
    }

    [Fact]
    public void StaysAtCap_WithoutOverflowing()
    {
        var backoff = new ReconnectBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        for (var i = 0; i < 100; i++)
            backoff.NextDelay();

        Assert.Equal(TimeSpan.FromSeconds(30), backoff.NextDelay());
    }
}
