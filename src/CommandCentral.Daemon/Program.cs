using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using CommandCentral.Daemon;
using CommandCentral.Daemon.Endpoints;
using CommandCentral.Input;
using CommandCentral.Input.Platform;
using CommandCentral.Output;
using VoiceToText.Abstractions;
using VoiceToText.DependencyInjection;
using VoiceToText.Audio.NAudio;

var builder = WebApplication.CreateBuilder(args);

// Bind Kestrel to configured host/port
// Defaults to localhost (safe). WSL2 mirrored networking reaches localhost directly.
var host = builder.Configuration.GetValue("CommandCentral:Server:Host", "127.0.0.1");
var port = builder.Configuration.GetValue("CommandCentral:Server:Port", 9000);
builder.WebHost.UseUrls($"http://{host}:{port}");

builder.Services.Configure<CommandCentralOptions>(
    builder.Configuration.GetSection("CommandCentral"));

// Core services
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<IInstanceRegistry>(sp =>
    new InMemoryInstanceRegistry(
        sp.GetRequiredService<IEventBus>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandCentralOptions>>()
            .Value.Instances.MaxInstances));
builder.Services.AddSingleton<IOrchestrator, Orchestrator>();

// Platform services — needed by Orchestrator and input pipeline
builder.Services.AddSingleton<IWindowManager, WindowsWindowManager>();
builder.Services.AddSingleton<IVirtualDesktopService, WindowsVirtualDesktopService>();
builder.Services.AddSingleton<InjectionBuffer>();
builder.Services.AddSingleton<DesktopNavigationContext>();

// Input/Output services — skip if COMMANDCENTRAL_HEADLESS_ONLY env var is set
// (used by integration tests to avoid hardware dependencies)
if (builder.Configuration["COMMANDCENTRAL_HEADLESS_ONLY"] is null &&
    Environment.GetEnvironmentVariable("COMMANDCENTRAL_HEADLESS_ONLY") is null)
{
    // STT: VoiceToText + Whisper + NAudio
    var sttConfig = builder.Configuration.GetSection("CommandCentral:Stt");
    builder.Services.AddVoiceToText();
    builder.Services.AddWhisperRecognizer(opts =>
    {
        var modelPath = sttConfig.GetValue("ModelPath", "../../models/ggml-tiny.bin")!;
        if (!Path.IsPathRooted(modelPath))
        {
            // Try relative to content root first (project root when running via dotnet run),
            // then fall back to base directory (output bin folder)
            var contentRoot = Path.Combine(builder.Environment.ContentRootPath, modelPath);
            var baseDir = Path.Combine(AppContext.BaseDirectory, modelPath);
            modelPath = File.Exists(contentRoot) ? contentRoot : baseDir;
        }
        opts.ModelPath = modelPath;
    });
    builder.Services.AddNAudioMicrophone();

    // Input services
    builder.Services.AddSingleton<IKeystrokeInjector, KeystrokeInjector>();
    builder.Services.AddSingleton<IAudioInputManager, AudioInputManager>();
    builder.Services.AddSingleton<PushToTalkHandler>();
    builder.Services.AddSingleton<HotkeyManager>();

    // Output services
    builder.Services.AddSingleton<VoiceAssigner>();
    builder.Services.AddSingleton<ITtsNotifier, TtsNotifier>();

    // Daemon hosted service (starts hotkey listener)
    builder.Services.AddHostedService<DaemonService>();

    // Buffered injection monitor (polls for cross-desktop text ready to inject)
    builder.Services.AddHostedService<BufferedInjectionMonitor>();
}

var app = builder.Build();

app.MapHookEndpoints();
app.MapApiEndpoints();

app.MapGet("/health", () => Results.Ok(new { Status = "running", Timestamp = DateTime.UtcNow }));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Command Central daemon starting on http://localhost:{Port}", port);

app.Run();

// Make Program accessible for WebApplicationFactory<Program> in integration tests
public partial class Program;
