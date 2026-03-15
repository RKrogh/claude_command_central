namespace CommandCentral.Core.Services;

public interface IKeystrokeInjector
{
    Task InjectTextAsync(nint windowHandle, string text, CancellationToken ct = default);
}
