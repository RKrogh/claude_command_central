using CommandCentral.Core.Models;
using CommandCentral.Core.Services;

namespace CommandCentral.Daemon.Endpoints;

public static class HookEndpoints
{
    public static void MapHookEndpoints(this WebApplication app)
    {
        var hooks = app.MapGroup("/hooks");

        hooks.MapPost("/session-start", async (HookPayload payload, IOrchestrator orchestrator, HttpContext ctx, CancellationToken ct) =>
        {
            var windowMarker = NonEmpty(ctx.Request.Query["wm"].FirstOrDefault());
            var wtSession = NonEmpty(ctx.Request.Query["wts"].FirstOrDefault());
            await orchestrator.HandleSessionStartAsync(payload, windowMarker, wtSession, ct);
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

        hooks.MapPost("/prompt-submit", async (HookPayload payload, IOrchestrator orchestrator, HttpContext ctx, CancellationToken ct) =>
        {
            var wtSession = NonEmpty(ctx.Request.Query["wts"].FirstOrDefault());
            await orchestrator.HandlePromptSubmitAsync(payload, wtSession, ct);
            return Results.Ok();
        });

        hooks.MapPost("/session-end", async (HookPayload payload, IOrchestrator orchestrator) =>
        {
            await orchestrator.HandleSessionEndAsync(payload);
            return Results.Ok();
        });
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
