using CommandCentral.Core.Models;
using CommandCentral.Core.Services;

namespace CommandCentral.Daemon.Endpoints;

public static class HookEndpoints
{
    public static void MapHookEndpoints(this WebApplication app)
    {
        var hooks = app.MapGroup("/hooks");

        hooks.MapPost("/session-start", async (HookPayload payload, IOrchestrator orchestrator) =>
        {
            await orchestrator.HandleSessionStartAsync(payload);
            return Results.Ok();
        });

        hooks.MapPost("/stop", async (HookPayload payload, IOrchestrator orchestrator) =>
        {
            await orchestrator.HandleStopAsync(payload);
            return Results.Ok();
        });

        hooks.MapPost("/notification", async (HookPayload payload, IOrchestrator orchestrator) =>
        {
            await orchestrator.HandleNotificationAsync(payload);
            return Results.Ok();
        });

        hooks.MapPost("/prompt-submit", async (HookPayload payload, IOrchestrator orchestrator) =>
        {
            await orchestrator.HandlePromptSubmitAsync(payload);
            return Results.Ok();
        });
    }
}
