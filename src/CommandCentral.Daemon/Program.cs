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
        opts.ModelPath = sttConfig.GetValue("ModelPath", "./models/ggml-base.en.bin")!;
    });
    builder.Services.AddNAudioMicrophone();

    // Input services
    builder.Services.AddSingleton<IWindowManager, WindowsWindowManager>();
    builder.Services.AddSingleton<IKeystrokeInjector, KeystrokeInjector>();
    builder.Services.AddSingleton<IAudioInputManager, AudioInputManager>();
    builder.Services.AddSingleton<PushToTalkHandler>();
    builder.Services.AddSingleton<HotkeyManager>();

    // Output services
    builder.Services.AddSingleton<VoiceAssigner>();
    builder.Services.AddSingleton<ITtsNotifier, TtsNotifier>();

    // Daemon hosted service (starts hotkey listener)
    builder.Services.AddHostedService<DaemonService>();
}

var app = builder.Build();

app.MapHookEndpoints();
app.MapApiEndpoints();

app.MapGet("/health", () => Results.Ok(new { Status = "running", Timestamp = DateTime.UtcNow }));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var port = app.Configuration.GetValue("CommandCentral:Server:Port", 9000);
logger.LogInformation("Command Central daemon starting on http://localhost:{Port}", port);

app.Run();

// Make Program accessible for WebApplicationFactory<Program> in integration tests
public partial class Program;
