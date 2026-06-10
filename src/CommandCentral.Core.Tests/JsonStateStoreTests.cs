using CommandCentral.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommandCentral.Core.Tests;

public class JsonStateStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"ccc-statestore-tests-{Guid.NewGuid():N}");

    private string StatePath => Path.Combine(_tempDir, "state.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private JsonStateStore CreateStore() =>
        new(StatePath, NullLogger<JsonStateStore>.Instance);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = CreateStore();

        Assert.Null(store.State.SelectedInstanceId);
        Assert.Empty(store.State.VoiceAssignments);
    }

    [Fact]
    public void Update_PersistsToDisk()
    {
        var store = CreateStore();

        store.Update(s => s.SelectedInstanceId = "3");

        Assert.True(File.Exists(StatePath));
        Assert.Contains("\"3\"", File.ReadAllText(StatePath));
    }

    [Fact]
    public void Update_RoundTripsAcrossInstances()
    {
        var store = CreateStore();
        store.Update(s =>
        {
            s.SelectedInstanceId = "2";
            s.VoiceAssignments["1"] = "en_US-lessac-medium";
            s.VoiceAssignments["2"] = "en_US-amy-medium";
        });

        // New store instance = simulated daemon restart
        var reloaded = CreateStore();

        Assert.Equal("2", reloaded.State.SelectedInstanceId);
        Assert.Equal("en_US-lessac-medium", reloaded.State.VoiceAssignments["1"]);
        Assert.Equal("en_US-amy-medium", reloaded.State.VoiceAssignments["2"]);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsWithoutThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(StatePath, "{ not valid json !!");

        var store = CreateStore();

        Assert.Null(store.State.SelectedInstanceId);
        Assert.Empty(store.State.VoiceAssignments);
    }

    [Fact]
    public void Update_AfterCorruptLoad_OverwritesWithValidState()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(StatePath, "garbage");

        var store = CreateStore();
        store.Update(s => s.SelectedInstanceId = "1");

        var reloaded = CreateStore();
        Assert.Equal("1", reloaded.State.SelectedInstanceId);
    }

    [Fact]
    public void Update_CreatesMissingDirectory()
    {
        Assert.False(Directory.Exists(_tempDir));

        var store = CreateStore();
        store.Update(s => s.SelectedInstanceId = "1");

        Assert.True(File.Exists(StatePath));
    }

    [Fact]
    public void State_ReturnsSnapshot_MutationsHaveNoEffect()
    {
        var store = CreateStore();
        store.Update(s => s.VoiceAssignments["1"] = "voice-a");

        var snapshot = store.State;
        snapshot.VoiceAssignments["1"] = "tampered";
        snapshot.SelectedInstanceId = "tampered";

        Assert.Equal("voice-a", store.State.VoiceAssignments["1"]);
        Assert.Null(store.State.SelectedInstanceId);
    }
}
