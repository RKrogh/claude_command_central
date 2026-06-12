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

// Settings edited from the TUI are persisted as a JSON overlay and layered
// on top of appsettings.json here. Headless/test mode skips the default
// overlay so developer machines don't leak state into tests; an explicit
// SettingsOverridesPath still applies (used by persistence tests).
var headless = builder.Configuration["COMMANDCENTRAL_HEADLESS_ONLY"] is not null ||
               Environment.GetEnvironmentVariable("COMMANDCENTRAL_HEADLESS_ONLY") is not null;
var overridesPath = builder.Configuration["CommandCentral:Persistence:SettingsOverridesPath"];
overridesPath = string.IsNullOrEmpty(overridesPath)
    ? (headless ? null : SettingsOverrideStore.DefaultPath)
    : Environment.ExpandEnvironmentVariables(overridesPath);
if (overridesPath is not null)
    builder.Configuration.AddJsonFile(overridesPath, optional: true, reloadOnChange: false);

builder.Services.Configure<CommandCentralOptions>(
    builder.Configuration.GetSection("CommandCentral"));

builder.Services.AddSingleton(sp => new SettingsOverrideStore(
    overridesPath, sp.GetRequiredService<ILogger<SettingsOverrideStore>>()));

// Resolve relative LocalTts model dir against content root (project root when
// running via dotnet run), falling back to the binary base directory —
// same approach as the Whisper model path below.
builder.Services.PostConfigure<CommandCentralOptions>(opts =>
{
    var modelsDir = Environment.ExpandEnvironmentVariables(opts.LocalTts.ModelsDir);
    if (!Path.IsPathRooted(modelsDir))
    {
        var contentRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, modelsDir));
        var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, modelsDir));
        modelsDir = Directory.Exists(contentRoot) ? contentRoot : baseDir;
    }
    opts.LocalTts.ModelsDir = modelsDir;
});

// Core services
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<IInstanceRegistry>(sp =>
    new InMemoryInstanceRegistry(
        sp.GetRequiredService<IEventBus>(),
        sp.GetRequiredService<IStateStore>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandCentralOptions>>()
            .Value.Instances.MaxInstances));
builder.Services.AddSingleton<IOrchestrator, Orchestrator>();
builder.Services.AddSingleton<InstanceActivityLog>();

// Platform services — needed by Orchestrator and input pipeline
builder.Services.AddSingleton<IWindowManager, WindowsWindowManager>();
builder.Services.AddSingleton<IWindowBindingService, WindowBindingService>();
builder.Services.AddSingleton<IVirtualDesktopService, WindowsVirtualDesktopService>();
builder.Services.AddSingleton<InjectionBuffer>();
builder.Services.AddSingleton<DesktopNavigationContext>();

// Input/Output services — skip if COMMANDCENTRAL_HEADLESS_ONLY env var is set
// (used by integration tests to avoid hardware dependencies)
if (builder.Configuration["COMMANDCENTRAL_HEADLESS_ONLY"] is null &&
    Environment.GetEnvironmentVariable("COMMANDCENTRAL_HEADLESS_ONLY") is null)
{
    // Persistent state (selected instance, voice assignments) — survives restarts
    builder.Services.AddSingleton<IStateStore>(sp =>
    {
        var configured = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandCentralOptions>>()
            .Value.Persistence.StateFilePath;
        var path = string.IsNullOrEmpty(configured)
            ? JsonStateStore.DefaultStateFilePath
            : Environment.ExpandEnvironmentVariables(configured);
        return new JsonStateStore(path, sp.GetRequiredService<ILogger<JsonStateStore>>());
    });

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
    builder.Services.AddSingleton<ResponseReadHandler>();
    builder.Services.AddSingleton<HotkeyManager>();

    // Output services
    builder.Services.AddSingleton<VoiceAssigner>();
    builder.Services.AddSingleton<IPersonalityManager, PersonalityManager>();
    builder.Services.AddSingleton<VoxtralEnginePool>();
    builder.Services.AddSingleton<SherpaOnnxEnginePool>();
    builder.Services.AddSingleton<ITtsEnginePool, TtsEnginePool>();
    builder.Services.AddSingleton<NotificationCache>();
    builder.Services.AddSingleton<INotificationCacheWarmer>(sp => sp.GetRequiredService<NotificationCache>());
    builder.Services.AddSingleton<ITtsNotifier, TtsNotifier>();

    // Hook authentication: shared secret auto-generated on first run
    builder.Services.AddSingleton<IHookSecretProvider, FileHookSecretProvider>();

    // Daemon hosted service (starts hotkey listener)
    builder.Services.AddHostedService<DaemonService>();

    // Buffered injection monitor (polls for cross-desktop text ready to inject)
    builder.Services.AddHostedService<BufferedInjectionMonitor>();
}
else
{
    // Headless mode: register noop implementations for Orchestrator dependencies
    builder.Services.AddSingleton<IStateStore, NoopStateStore>();
    builder.Services.AddSingleton<ITtsNotifier, NoopTtsNotifier>();
    builder.Services.AddSingleton<IPersonalityManager, NoopPersonalityManager>();
    builder.Services.AddSingleton<IKeystrokeInjector, NoopKeystrokeInjector>();
    builder.Services.AddSingleton<INotificationCacheWarmer, NoopNotificationCacheWarmer>();
    builder.Services.AddSingleton<IHookSecretProvider, ConfigHookSecretProvider>();
}

var app = builder.Build();

// Instantiate eagerly so it subscribes to the event bus before the first
// hook arrives — otherwise early activity would be missing from the log.
// This must also happen before the server accepts connections: the bus
// dispatches in subscription order, so subscribing the activity log first
// guarantees every EventStreamSocket (subscribed per connection, later)
// builds its event DTOs *after* the log has recorded the event, keeping
// RecentActivity in sync with the event being relayed.
_ = app.Services.GetRequiredService<InstanceActivityLog>();

app.UseWebSockets();

app.MapHookEndpoints();
app.MapApiEndpoints();
app.MapConfigEndpoints();

app.MapGet("/health", () => Results.Ok(new { Status = "running", Timestamp = DateTime.UtcNow }));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Command Central daemon starting on http://localhost:{Port}", port);

app.Run();

// Make Program accessible for WebApplicationFactory<Program> in integration tests
public partial class Program;
