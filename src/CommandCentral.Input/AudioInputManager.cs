using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging;
using VoiceToText.Abstractions;
using VoiceToText.Audio;
using VoiceToText.Models;

namespace CommandCentral.Input;

public sealed class AudioInputManager(
    IAudioSource audioSource,
    ISpeechRecognizer recognizer,
    ILogger<AudioInputManager> logger) : IAudioInputManager, IDisposable
{
    private MemoryStream? _captureBuffer;
    private readonly Lock _lock = new();

    public bool IsCapturing => audioSource.IsCapturing;

    public async Task StartCaptureAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _captureBuffer?.Dispose();
            _captureBuffer = new MemoryStream();
        }

        audioSource.DataAvailable += OnDataAvailable;
        await audioSource.StartAsync(ct);
        logger.LogDebug("Mic capture started");
    }

    public async Task<string> StopCaptureAndTranscribeAsync(CancellationToken ct = default)
    {
        await audioSource.StopAsync(ct);
        audioSource.DataAvailable -= OnDataAvailable;
        logger.LogDebug("Mic capture stopped");

        MemoryStream buffer;
        lock (_lock)
        {
            buffer = _captureBuffer ?? new MemoryStream();
            _captureBuffer = null;
        }

        if (buffer.Length == 0)
        {
            logger.LogDebug("No audio captured");
            buffer.Dispose();
            return string.Empty;
        }

        buffer.Position = 0;

        var wavStream = WrapPcmAsWav(buffer, audioSource.Format);

        try
        {
            var result = await recognizer.TranscribeAsync(wavStream, new RecognizerOptions(), ct);
            var text = result.Text?.Trim() ?? string.Empty;
            logger.LogInformation("STT result ({Length} chars): {Preview}",
                text.Length, text.Length > 80 ? text[..80] + "..." : text);
            return text;
        }
        finally
        {
            wavStream.Dispose();
            buffer.Dispose();
        }
    }

    private void OnDataAvailable(object? sender, AudioDataEventArgs e)
    {
        lock (_lock)
        {
            _captureBuffer?.Write(e.Buffer.Span);
        }
    }

    private static MemoryStream WrapPcmAsWav(MemoryStream pcmData, AudioFormat format)
    {
        var wav = new MemoryStream();
        using var writer = new BinaryWriter(wav, System.Text.Encoding.UTF8, leaveOpen: true);

        var sampleRate = format.SampleRate;
        var channels = (short)format.Channels;
        var bitsPerSample = (short)format.BitsPerSample;
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        var dataLength = (int)pcmData.Length;

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);

        // fmt chunk
        writer.Write("fmt "u8);
        writer.Write(16); // chunk size
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        // data chunk
        writer.Write("data"u8);
        writer.Write(dataLength);

        pcmData.Position = 0;
        pcmData.CopyTo(wav);

        wav.Position = 0;
        return wav;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _captureBuffer?.Dispose();
            _captureBuffer = null;
        }
    }
}
