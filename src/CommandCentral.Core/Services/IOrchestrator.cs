using CommandCentral.Core.Models;

namespace CommandCentral.Core.Services;

public interface IOrchestrator
{
    Task HandleSessionStartAsync(HookPayload payload, string? windowMarker = null, CancellationToken ct = default);
    Task HandleStopAsync(HookPayload payload, CancellationToken ct = default);
    Task HandleNotificationAsync(HookPayload payload, CancellationToken ct = default);
    Task HandlePromptSubmitAsync(HookPayload payload, CancellationToken ct = default);
    Task HandleSessionEndAsync(HookPayload payload, CancellationToken ct = default);
}
