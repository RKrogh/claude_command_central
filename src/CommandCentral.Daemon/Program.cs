using CommandCentral.Core.Configuration;
using CommandCentral.Core.Services;
using CommandCentral.Daemon;
using CommandCentral.Daemon.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CommandCentralOptions>(
    builder.Configuration.GetSection("CommandCentral"));

builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<IInstanceRegistry>(sp =>
    new InMemoryInstanceRegistry(
        sp.GetRequiredService<IEventBus>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandCentralOptions>>()
            .Value.Instances.MaxInstances));
builder.Services.AddSingleton<IOrchestrator, Orchestrator>();

var app = builder.Build();

app.MapHookEndpoints();
app.MapApiEndpoints();

app.MapGet("/health", () => Results.Ok(new { Status = "running", Timestamp = DateTime.UtcNow }));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Command Central daemon starting on {Url}", $"http://localhost:{app.Configuration.GetValue("CommandCentral:Server:Port", 9000)}");

app.Run();

// Make Program accessible for WebApplicationFactory<Program> in integration tests
public partial class Program;
