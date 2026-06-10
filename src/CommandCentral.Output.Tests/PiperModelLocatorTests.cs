namespace CommandCentral.Output.Tests;

public class PiperModelLocatorTests : IDisposable
{
    private readonly TempDir _tempDir = new();

    public void Dispose() => _tempDir.Dispose();

    private string CreateModelDir(string dirName, string voice, bool tokens = true, bool dataDir = true)
    {
        var dir = Path.Combine(_tempDir.Path, dirName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{voice}.onnx"), "fake-model");
        if (tokens)
            File.WriteAllText(Path.Combine(dir, "tokens.txt"), "fake-tokens");
        if (dataDir)
            Directory.CreateDirectory(Path.Combine(dir, "espeak-ng-data"));
        return dir;
    }

    [Fact]
    public void Locate_MissingModelsDir_ReturnsNull()
    {
        var result = PiperModelLocator.Locate(
            Path.Combine(_tempDir.Path, "does-not-exist"), "en_US-lessac-medium");

        Assert.Null(result);
    }

    [Fact]
    public void Locate_VitsPiperPrefixedDir_ReturnsModel()
    {
        CreateModelDir("vits-piper-en_US-lessac-medium", "en_US-lessac-medium");

        var result = PiperModelLocator.Locate(_tempDir.Path, "en_US-lessac-medium");

        Assert.NotNull(result);
        Assert.Equal("en_US-lessac-medium", result.Voice);
        Assert.EndsWith("en_US-lessac-medium.onnx", result.ModelPath);
        Assert.True(File.Exists(result.TokensPath));
        Assert.True(Directory.Exists(result.DataDir));
    }

    [Fact]
    public void Locate_BareVoiceDir_ReturnsModel()
    {
        CreateModelDir("en_US-amy-medium", "en_US-amy-medium");

        var result = PiperModelLocator.Locate(_tempDir.Path, "en_US-amy-medium");

        Assert.NotNull(result);
        Assert.Equal("en_US-amy-medium", result.Voice);
    }

    [Fact]
    public void Locate_MissingTokens_ReturnsNull()
    {
        CreateModelDir("vits-piper-en_US-lessac-medium", "en_US-lessac-medium", tokens: false);

        var result = PiperModelLocator.Locate(_tempDir.Path, "en_US-lessac-medium");

        Assert.Null(result);
    }

    [Fact]
    public void Locate_MissingEspeakData_ReturnsNull()
    {
        CreateModelDir("vits-piper-en_US-lessac-medium", "en_US-lessac-medium", dataDir: false);

        var result = PiperModelLocator.Locate(_tempDir.Path, "en_US-lessac-medium");

        Assert.Null(result);
    }

    [Fact]
    public void ListAvailableVoices_MissingDir_ReturnsEmpty()
    {
        var voices = PiperModelLocator.ListAvailableVoices(
            Path.Combine(_tempDir.Path, "does-not-exist"));

        Assert.Empty(voices);
    }

    [Fact]
    public void ListAvailableVoices_ReturnsOnlyCompleteModels()
    {
        CreateModelDir("vits-piper-en_US-lessac-medium", "en_US-lessac-medium");
        CreateModelDir("vits-piper-en_US-amy-medium", "en_US-amy-medium");
        CreateModelDir("vits-piper-en_US-broken", "en_US-broken", tokens: false);

        var voices = PiperModelLocator.ListAvailableVoices(_tempDir.Path);

        Assert.Equal(["en_US-amy-medium", "en_US-lessac-medium"], voices);
    }
}
