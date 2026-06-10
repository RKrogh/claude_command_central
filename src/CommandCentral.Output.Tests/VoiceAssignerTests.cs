using CommandCentral.Core.Configuration;
using CommandCentral.Core.Models;
using Microsoft.Extensions.Options;

namespace CommandCentral.Output.Tests;

public class VoiceAssignerTests
{
    private static IOptions<CommandCentralOptions> Options(Action<CommandCentralOptions>? configure = null)
    {
        var options = new CommandCentralOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    [Fact]
    public void AssignVoice_AutoAssignsDistinctVoices()
    {
        var assigner = new VoiceAssigner(Options(), new InMemoryStateStore());

        var voice1 = assigner.AssignVoice("1");
        var voice2 = assigner.AssignVoice("2");

        Assert.NotEqual(voice1, voice2);
    }

    [Fact]
    public void AssignVoice_IsStableForSameSlot()
    {
        var assigner = new VoiceAssigner(Options(), new InMemoryStateStore());

        var first = assigner.AssignVoice("1");
        var second = assigner.AssignVoice("1");

        Assert.Equal(first, second);
    }

    [Fact]
    public void AssignVoice_PersistsToStateStore()
    {
        var store = new InMemoryStateStore();
        var assigner = new VoiceAssigner(Options(), store);

        var voice = assigner.AssignVoice("3");

        Assert.Equal(voice, store.State.VoiceAssignments["3"]);
    }

    [Fact]
    public void AssignVoice_ReusesPersistedAssignmentAcrossRestarts()
    {
        var store = new InMemoryStateStore(new DaemonState
        {
            VoiceAssignments = new Dictionary<string, string> { ["2"] = "en_US-ryan-medium" }
        });

        // New assigner = simulated daemon restart
        var assigner = new VoiceAssigner(Options(), store);

        Assert.Equal("en_US-ryan-medium", assigner.AssignVoice("2"));
    }

    [Fact]
    public void AssignVoice_ExplicitConfigOverridesPersistedAssignment()
    {
        var store = new InMemoryStateStore(new DaemonState
        {
            VoiceAssignments = new Dictionary<string, string> { ["1"] = "en_US-ryan-medium" }
        });
        var options = Options(o => o.Tts.Voices["1"] = new VoiceOptions { Name = "en_US-amy-medium" });

        var assigner = new VoiceAssigner(options, store);

        Assert.Equal("en_US-amy-medium", assigner.AssignVoice("1"));
        Assert.Equal("en_US-amy-medium", store.State.VoiceAssignments["1"]);
    }

    [Fact]
    public void ReleaseVoice_RemovesAssignmentAndPersists()
    {
        var store = new InMemoryStateStore();
        var assigner = new VoiceAssigner(Options(), store);
        assigner.AssignVoice("1");

        assigner.ReleaseVoice("1");

        Assert.Null(assigner.GetAssignedVoice("1"));
        Assert.False(store.State.VoiceAssignments.ContainsKey("1"));
    }

    [Fact]
    public void AssignVoice_RepeatedCalls_DoNotRewriteState()
    {
        var store = new InMemoryStateStore();
        var assigner = new VoiceAssigner(Options(), store);

        assigner.AssignVoice("1");
        var writesAfterFirst = store.UpdateCount;
        assigner.AssignVoice("1");
        assigner.AssignVoice("1");

        Assert.Equal(writesAfterFirst, store.UpdateCount);
    }
}
