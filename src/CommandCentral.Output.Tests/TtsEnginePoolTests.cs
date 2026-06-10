using CommandCentral.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommandCentral.Output.Tests;

public class TtsEnginePoolTests : IDisposable
{
    private readonly TempDir _tempDir = new();
    private readonly TestLogger<TtsEnginePool> _logger = new();

    public void Dispose() => _tempDir.Dispose();

    private TtsEnginePool CreatePool(string notificationEngine, string? voxtralApiKey = null)
    {
        var options = new CommandCentralOptions();
        options.Tts.NotificationEngine = notificationEngine;
        options.LocalTts.ModelsDir = Path.Combine(_tempDir.Path, "models");
        options.Voxtral.ApiKey = voxtralApiKey;
        var wrapped = Options.Create(options);

        var personalityManager = new StubPersonalityManager();
        return new TtsEnginePool(
            wrapped,
            new SherpaOnnxEnginePool(
                wrapped,
                new VoiceAssigner(wrapped, new InMemoryStateStore()),
                new TestLogger<SherpaOnnxEnginePool>()),
            new VoxtralEnginePool(wrapped, personalityManager, new TestLogger<VoxtralEnginePool>()),
            personalityManager,
            _logger);
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("None")]
    [InlineData("disabled")]
    [InlineData("")]
    public void GetOrCreate_DisabledEngine_ReturnsNullSilently(string engine)
    {
        var pool = CreatePool(engine);

        Assert.Null(pool.GetOrCreate("1"));
        Assert.Equal(0, _logger.Count(LogLevel.Warning));
    }

    [Fact]
    public void GetOrCreate_NullEngineConfig_DefaultsToSherpaWithoutThrowing()
    {
        // Config binding can explicitly null out the value; must not NRE.
        var pool = CreatePool(null!);

        Assert.Null(pool.GetOrCreate("1"));
        Assert.Equal("sherpa:none", pool.GetVoiceCacheKey("1"));
        Assert.Equal(0, _logger.Count(LogLevel.Warning));
    }

    [Fact]
    public void GetOrCreate_UnknownEngine_ReturnsNullAndWarnsOnce()
    {
        var pool = CreatePool("Festival");

        Assert.Null(pool.GetOrCreate("1"));
        Assert.Null(pool.GetOrCreate("2"));

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("Festival", warning);
    }

    [Fact]
    public void GetOrCreate_SherpaWithoutModel_ReturnsNull()
    {
        var pool = CreatePool("SherpaOnnx");

        Assert.Null(pool.GetOrCreate("1"));
    }

    [Fact]
    public void GetOrCreate_VoxtralWithoutApiKey_ReturnsNull()
    {
        var pool = CreatePool("Voxtral");

        Assert.Null(pool.GetOrCreate("1"));
    }

    [Fact]
    public void GetVoiceCacheKey_SherpaWithoutModel_ReturnsSherpaNone()
    {
        var pool = CreatePool("SherpaOnnx");

        Assert.Equal("sherpa:none", pool.GetVoiceCacheKey("1"));
    }

    [Fact]
    public void GetVoiceCacheKey_Voxtral_UsesDefaultVoiceId()
    {
        var pool = CreatePool("Voxtral", voxtralApiKey: "test-key");

        Assert.Equal("voxtral:gb_jane_neutral", pool.GetVoiceCacheKey("1"));
    }

    [Theory]
    [InlineData("SherpaOnnx")]
    [InlineData("Voxtral")]
    [InlineData("Disabled")]
    [InlineData("Bogus")]
    public void LogStartupDiagnostics_NeverThrows(string engine)
    {
        var pool = CreatePool(engine);

        pool.LogStartupDiagnostics();

        Assert.NotEmpty(_logger.Entries);
    }

    [Fact]
    public void LogStartupDiagnostics_SherpaWithoutModel_WarnsWithDownloadInstructions()
    {
        var pool = CreatePool("SherpaOnnx");

        pool.LogStartupDiagnostics();

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("download-tts-model", warning);
    }

    [Fact]
    public void LogStartupDiagnostics_VoxtralWithoutApiKey_WarnsWithUserSecretsHint()
    {
        var pool = CreatePool("Voxtral");

        pool.LogStartupDiagnostics();

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("user-secrets", warning);
    }
}
