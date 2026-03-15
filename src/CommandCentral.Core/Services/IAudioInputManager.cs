namespace CommandCentral.Core.Services;

public interface IAudioInputManager
{
    Task StartCaptureAsync(CancellationToken ct = default);
    Task<string> StopCaptureAndTranscribeAsync(CancellationToken ct = default);
    bool IsCapturing { get; }
}
