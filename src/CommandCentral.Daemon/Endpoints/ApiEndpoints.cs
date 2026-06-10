using CommandCentral.Core.Services;
using CommandCentral.Daemon.EventStream;

namespace CommandCentral.Daemon.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/state", (IInstanceRegistry registry, InstanceActivityLog activityLog) =>
            Results.Ok(ApiMapper.BuildSnapshot(registry, activityLog)));

        // WebSocket event stream: snapshot on connect, then live instance and
        // daemon events. The TUI is the primary consumer.
        api.Map("/events", async (
            HttpContext context,
            IInstanceRegistry registry,
            InstanceActivityLog activityLog,
            IEventBus eventBus,
            ILogger<EventStreamSocket> logger) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("WebSocket connection required.");
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var stream = new EventStreamSocket(registry, activityLog, eventBus, logger);
            await stream.RunAsync(socket, context.RequestAborted);
        });
    }
}
