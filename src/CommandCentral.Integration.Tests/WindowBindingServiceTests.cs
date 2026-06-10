using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using CommandCentral.Input;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommandCentral.Integration.Tests;

public class WindowBindingServiceTests
{
    private readonly FakeWindowManager _windowManager = new();
    private readonly InMemoryInstanceRegistry _registry = new(new InMemoryEventBus());
    private readonly WindowBindingService _service;

    public WindowBindingServiceTests()
    {
        _service = new WindowBindingService(
            _windowManager, _registry, NullLogger<WindowBindingService>.Instance)
        {
            MarkerPropagationDelay = TimeSpan.Zero
        };
    }

    private InstanceInfo RegisterInstance(string sessionId = "s1") =>
        _registry.Register(sessionId);

    [Fact]
    public async Task BindOnSessionStart_MatchesTitleMarker()
    {
        _windowManager.WindowsByTitle["cc:abcd1234 - Terminal"] = 0x100;
        _windowManager.ForegroundWindow = 0x200;
        var instance = RegisterInstance();

        var handle = await _service.BindOnSessionStartAsync(instance, "abcd1234");

        Assert.Equal(0x100, handle);
        Assert.Equal(0x100, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.TitleMarker, instance.WindowBindingSource);
        Assert.NotNull(instance.WindowBoundAt);
    }

    [Fact]
    public async Task BindOnSessionStart_FallsBackToForeground_WhenMarkerMisses()
    {
        _windowManager.ForegroundWindow = 0x200;
        var instance = RegisterInstance();

        var handle = await _service.BindOnSessionStartAsync(instance, "deadbeef");

        Assert.Equal(0x200, handle);
        Assert.Equal(WindowBindingSource.SessionStartForeground, instance.WindowBindingSource);
    }

    [Fact]
    public async Task BindOnSessionStart_FallsBackToForeground_WhenNoMarker()
    {
        _windowManager.ForegroundWindow = 0x200;
        var instance = RegisterInstance();

        var handle = await _service.BindOnSessionStartAsync(instance, windowMarker: null);

        Assert.Equal(0x200, handle);
        Assert.Equal(WindowBindingSource.SessionStartForeground, instance.WindowBindingSource);
    }

    [Fact]
    public async Task BindOnSessionStart_ReturnsZero_WhenNothingAvailable()
    {
        var instance = RegisterInstance();

        var handle = await _service.BindOnSessionStartAsync(instance, "deadbeef");

        Assert.Equal(nint.Zero, handle);
        Assert.Equal(WindowBindingSource.None, instance.WindowBindingSource);
    }

    [Fact]
    public async Task BindOnSessionStart_AllowsSharedHandle_AcrossInstances()
    {
        // Two Claude Code tabs in the same Windows Terminal window share one HWND.
        _windowManager.ForegroundWindow = 0x300;
        var first = RegisterInstance("s1");
        var second = RegisterInstance("s2");
        first.WindowHandle = 0x300;

        var handle = await _service.BindOnSessionStartAsync(second, windowMarker: null);

        Assert.Equal(0x300, handle);
        Assert.Equal(0x300, second.WindowHandle);
    }

    [Fact]
    public async Task ClaimForeground_PromptSubmitOverwritesSessionStartClaim()
    {
        var instance = RegisterInstance();
        instance.WindowHandle = 0x100;
        instance.WindowBindingSource = WindowBindingSource.SessionStartForeground;
        _windowManager.ForegroundWindow = 0x500;

        var claimed = await _service.ClaimForegroundAsync(instance, WindowBindingSource.PromptSubmit);

        Assert.True(claimed);
        Assert.Equal(0x500, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.PromptSubmit, instance.WindowBindingSource);
    }

    [Fact]
    public async Task ClaimForeground_KeepsBinding_WhenNoForegroundWindow()
    {
        var instance = RegisterInstance();
        instance.WindowHandle = 0x100;
        instance.WindowBindingSource = WindowBindingSource.SessionStartForeground;
        _windowManager.ForegroundWindow = nint.Zero;

        var claimed = await _service.ClaimForegroundAsync(instance, WindowBindingSource.PromptSubmit);

        Assert.False(claimed);
        Assert.Equal(0x100, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.SessionStartForeground, instance.WindowBindingSource);
    }

    [Fact]
    public async Task ClaimForeground_ManualBindingSurvivesPromptSubmit()
    {
        var instance = RegisterInstance();
        instance.WindowHandle = 0x100;
        instance.WindowBindingSource = WindowBindingSource.Manual;
        _windowManager.ForegroundWindow = 0x500;

        var claimed = await _service.ClaimForegroundAsync(instance, WindowBindingSource.PromptSubmit);

        Assert.False(claimed);
        Assert.Equal(0x100, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.Manual, instance.WindowBindingSource);
    }

    [Fact]
    public async Task ClaimForeground_ManualRebindReplacesManualBinding()
    {
        var instance = RegisterInstance();
        instance.WindowHandle = 0x100;
        instance.WindowBindingSource = WindowBindingSource.Manual;
        _windowManager.ForegroundWindow = 0x500;

        var claimed = await _service.ClaimForegroundAsync(instance, WindowBindingSource.Manual);

        Assert.True(claimed);
        Assert.Equal(0x500, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.Manual, instance.WindowBindingSource);
    }
}
