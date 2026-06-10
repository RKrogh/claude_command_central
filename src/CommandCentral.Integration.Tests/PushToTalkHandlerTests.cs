using CommandCentral.Core.Models;
using CommandCentral.Core.Services;
using CommandCentral.Input;
using CommandCentral.Input.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommandCentral.Integration.Tests;

public class PushToTalkHandlerTests
{
    private readonly FakeAudioInputManager _audioInput = new();
    private readonly FakeWindowManager _windowManager = new();
    private readonly InMemoryInstanceRegistry _registry = new(new InMemoryEventBus());
    private readonly PushToTalkHandler _handler;

    public PushToTalkHandlerTests()
    {
        var eventBus = new InMemoryEventBus();
        var windowBinding = new WindowBindingService(
            _windowManager, _registry, NullLogger<WindowBindingService>.Instance);

        _handler = new PushToTalkHandler(
            _audioInput,
            new FakeKeystrokeInjector(),
            _registry,
            eventBus,
            new UnavailableVirtualDesktopService(),
            windowBinding,
            new InjectionBuffer(eventBus, NullLogger<InjectionBuffer>.Instance),
            new DesktopNavigationContext(),
            NullLogger<PushToTalkHandler>.Instance);
    }

    [Fact]
    public async Task Start_ClaimsForeground_WhenInstanceUnbound()
    {
        var instance = _registry.Register("s1");
        Assert.Equal(nint.Zero, instance.WindowHandle);
        _windowManager.ForegroundWindow = 0x42;

        await _handler.StartAsync(instance.Id);

        Assert.Equal(0x42, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.PttClaim, instance.WindowBindingSource);
    }

    [Fact]
    public async Task Start_KeepsExistingBinding()
    {
        var instance = _registry.Register("s1");
        instance.WindowHandle = 0x10;
        instance.WindowBindingSource = WindowBindingSource.PromptSubmit;
        _windowManager.ForegroundWindow = 0x42;

        await _handler.StartAsync(instance.Id);

        Assert.Equal(0x10, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.PromptSubmit, instance.WindowBindingSource);
    }

    [Fact]
    public async Task StartFocusPtt_ClaimsForeground_WhenInstanceUnbound()
    {
        var instance = _registry.Register("s1");
        _windowManager.ForegroundWindow = 0x42;

        await _handler.StartFocusPttAsync(instance.Id);

        Assert.Equal(0x42, instance.WindowHandle);
        Assert.Equal(WindowBindingSource.PttClaim, instance.WindowBindingSource);
    }

    private sealed class FakeAudioInputManager : IAudioInputManager
    {
        public bool IsCapturing { get; private set; }

        public Task StartCaptureAsync(CancellationToken ct = default)
        {
            IsCapturing = true;
            return Task.CompletedTask;
        }

        public Task<string> StopCaptureAndTranscribeAsync(CancellationToken ct = default)
        {
            IsCapturing = false;
            return Task.FromResult(string.Empty);
        }
    }

    private sealed class FakeKeystrokeInjector : IKeystrokeInjector
    {
        public Task InjectTextAsync(nint windowHandle, string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task InjectTextAndSubmitAsync(nint windowHandle, string text, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class UnavailableVirtualDesktopService : IVirtualDesktopService
    {
        public bool IsAvailable => false;
        public bool IsWindowOnCurrentDesktop(nint hwnd) => true;
        public Task SwitchToDesktopOfWindowAsync(nint hwnd, CancellationToken ct = default) => Task.CompletedTask;
        public DesktopContext GetCurrentContext() => default;
        public Task RestoreContextAsync(DesktopContext context, CancellationToken ct = default) => Task.CompletedTask;
    }
}
