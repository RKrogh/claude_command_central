using CommandCentral.Core.Services;

namespace CommandCentral.Daemon.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/state", (IInstanceRegistry registry) =>
        {
            var instances = registry.GetAll().Select(i => new
            {
                i.Id,
                i.SessionId,
                i.Cwd,
                i.ProjectName,
                State = i.State.ToString(),
                i.VoiceProfile,
                i.LastActivity
            });

            return Results.Ok(new
            {
                SelectedInstanceId = registry.SelectedInstanceId,
                Instances = instances
            });
        });

        // WebSocket endpoint for TUI will be added in Phase 3
    }
}
