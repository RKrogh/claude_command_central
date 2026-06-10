using CommandCentral.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommandCentral.Output.Tests;

public class SherpaOnnxEnginePoolTests : IDisposable
{
    private readonly TempDir _tempDir = new();
    private readonly TestLogger<SherpaOnnxEnginePool> _logger = new();

    public void Dispose() => _tempDir.Dispose();

    private SherpaOnnxEnginePool CreatePool(string? modelsDir = null)
    {
        var options = new CommandCentralOptions();
        options.LocalTts.ModelsDir = modelsDir ?? Path.Combine(_tempDir.Path, "empty");

        return new SherpaOnnxEnginePool(
            Options.Create(options),
            new VoiceAssigner(Options.Create(options), new InMemoryStateStore()),
            _logger);
    }

    [Fact]
    public void GetOrCreate_NoModels_ReturnsNull()
    {
        using var pool = CreatePool();

        Assert.Null(pool.GetOrCreate("1"));
    }

    [Fact]
    public void GetOrCreate_NoModels_WarnsExactlyOnce()
    {
        using var pool = CreatePool();

        pool.GetOrCreate("1");
        pool.GetOrCreate("2");
        pool.GetOrCreate("1");

        Assert.Equal(1, _logger.Count(LogLevel.Warning));
        Assert.Equal(0, _logger.Count(LogLevel.Error));
    }

    [Fact]
    public void GetOrCreate_NoModels_WarningExplainsWhatToDownload()
    {
        using var pool = CreatePool();

        pool.GetOrCreate("1");

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("download-tts-model", warning);
        Assert.Contains(SherpaOnnxEnginePool.DownloadBaseUrl, warning);
        Assert.Contains("en_US-lessac-medium", warning);
    }

    [Fact]
    public void ResolveVoiceKey_NoModels_ReturnsNone()
    {
        using var pool = CreatePool();

        Assert.Equal("none", pool.ResolveVoiceKey("1"));
    }

    [Fact]
    public void HasAnyModel_EmptyDir_ReturnsFalse()
    {
        using var pool = CreatePool();

        Assert.False(pool.HasAnyModel());
    }
}
