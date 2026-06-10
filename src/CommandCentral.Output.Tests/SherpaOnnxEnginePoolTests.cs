using CommandCentral.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextToVoice.Core;

namespace CommandCentral.Output.Tests;

public class SherpaOnnxEnginePoolTests : IDisposable
{
    private readonly TempDir _tempDir = new();
    private readonly TestLogger<SherpaOnnxEnginePool> _logger = new();

    public void Dispose() => _tempDir.Dispose();

    private string ModelsDir => Path.Combine(_tempDir.Path, "models");

    private SherpaOnnxEnginePool CreatePool(
        string? modelsDir = null,
        Func<PiperModelLocator.PiperModel, ITtsEngine>? engineFactory = null,
        Action<CommandCentralOptions>? configure = null)
    {
        var options = new CommandCentralOptions();
        options.LocalTts.ModelsDir = modelsDir ?? Path.Combine(_tempDir.Path, "empty");
        configure?.Invoke(options);
        var wrapped = Options.Create(options);

        return new SherpaOnnxEnginePool(
            wrapped,
            new VoiceAssigner(wrapped, new InMemoryStateStore()),
            _logger,
            engineFactory);
    }

    private void CreateFakeModel(string voice)
    {
        var dir = Path.Combine(ModelsDir, $"vits-piper-{voice}");
        Directory.CreateDirectory(Path.Combine(dir, "espeak-ng-data"));
        File.WriteAllText(Path.Combine(dir, $"{voice}.onnx"), "fake-model");
        File.WriteAllText(Path.Combine(dir, "tokens.txt"), "fake-tokens");
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

    [Fact]
    public void GetOrCreate_AssignedModelFailsToLoad_FallsBackToDefaultVoice()
    {
        CreateFakeModel("en_US-amy-medium");
        CreateFakeModel("en_US-lessac-medium");

        var fallbackEngine = new StubTtsEngine();
        using var pool = CreatePool(
            ModelsDir,
            engineFactory: model => model.Voice == "en_US-amy-medium"
                ? throw new InvalidOperationException("broken model")
                : fallbackEngine,
            configure: o => o.Tts.Voices["1"] = new VoiceOptions { Name = "en_US-amy-medium" });

        Assert.Same(fallbackEngine, pool.GetOrCreate("1"));
        Assert.Equal(1, _logger.Count(LogLevel.Error));
    }

    [Fact]
    public void GetOrCreate_FailedVoice_LogsErrorOnceAndDoesNotRetry()
    {
        CreateFakeModel("en_US-lessac-medium");

        var attempts = 0;
        using var pool = CreatePool(ModelsDir, engineFactory: _ =>
        {
            attempts++;
            throw new InvalidOperationException("broken model");
        });

        Assert.Null(pool.GetOrCreate("1"));
        Assert.Null(pool.GetOrCreate("1"));

        Assert.Equal(1, attempts);
        Assert.Equal(1, _logger.Count(LogLevel.Error));
    }

    [Fact]
    public void ResolveVoiceKey_SkipsVoicesThatFailedToLoad()
    {
        CreateFakeModel("en_US-amy-medium");
        CreateFakeModel("en_US-lessac-medium");

        using var pool = CreatePool(
            ModelsDir,
            engineFactory: model => model.Voice == "en_US-amy-medium"
                ? throw new InvalidOperationException("broken model")
                : new StubTtsEngine(),
            configure: o => o.Tts.Voices["1"] = new VoiceOptions { Name = "en_US-amy-medium" });

        pool.GetOrCreate("1");

        // The cache key must match the engine actually used, not the broken voice.
        Assert.Equal("en_US-lessac-medium", pool.ResolveVoiceKey("1"));
    }

    [Fact]
    public void GetOrCreate_AfterDispose_ReturnsNullWithoutCreatingEngines()
    {
        CreateFakeModel("en_US-lessac-medium");

        var created = 0;
        var pool = CreatePool(ModelsDir, engineFactory: _ =>
        {
            created++;
            return new StubTtsEngine();
        });

        pool.Dispose();

        Assert.Null(pool.GetOrCreate("1"));
        Assert.Equal(0, created);
    }

    [Fact]
    public void Dispose_DisposesCreatedEngines()
    {
        CreateFakeModel("en_US-lessac-medium");

        var engine = new StubTtsEngine();
        var pool = CreatePool(ModelsDir, engineFactory: _ => engine);
        Assert.Same(engine, pool.GetOrCreate("1"));

        pool.Dispose();

        Assert.True(engine.Disposed);
    }
}
