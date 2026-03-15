using CommandCentral.Core.Models;

namespace CommandCentral.Core.Services;

public interface IOrchestrator
{
    Task HandleSessionStartAsync(HookPayload payload, CancellationToken ct = default);
    Task HandleStopAsync(HookPayload payload, CancellationToken ct = default);
    Task HandleNotificationAsync(HookPayload payload, CancellationToken ct = default);
    Task HandlePromptSubmitAsync(HookPayload payload, CancellationToken ct = default);
}
