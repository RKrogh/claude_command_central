using CommandCentral.Core.Api;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Tests;

public class SettingsModelTests
{
    private static ConfigDto SampleConfig() => new()
    {
        Hotkeys = new HotkeyConfigDto
        {
            LeaderKey = "Ctrl+Shift+Q",
            LeaderTimeoutMs = 2000,
            PttBindings = "1-9",
            FocusBindings = "Shift+1-9",
            ReadResponseBindings = "Ctrl+1-9",
            PttSelectedInstance = "Space",
            ReadResponseSelected = "P",
            CycleInstance = "Tab",
            QuickBack = "BackQuote",
            MuteAll = "M",
            RebindWindow = "R",
        },
        Tts = new TtsConfigDto { NotificationEngine = "SherpaOnnx", ResponseEngine = "Voxtral", MaxResponseChars = 1500 },
        SttLanguage = "en",
        MaxInstances = 25,
        HookAuthEnabled = true,
    };

    [Fact]
    public void Build_WithConfig_ContainsLeaderKeyAndEditableRows()
    {
        var rows = SettingsModel.Build(SampleConfig(), "http://localhost:9000");

        var leader = rows.Single(r => r.Key == "hk.leader");
        Assert.Contains("Ctrl+Shift+Q", leader.Value);
        Assert.False(leader.Editable);

        Assert.Equal(3, rows.Count(r => r.Editable));
        Assert.Contains(rows, r => r.Key == SettingsModel.KeyResponseEngine && r.Value == "Voxtral");
    }

    [Fact]
    public void Build_WithoutConfig_ShowsUnreachableHint()
    {
        var rows = SettingsModel.Build(null, "http://localhost:9000");

        Assert.Contains(rows, r => r.Value.Contains("not loaded"));
        Assert.DoesNotContain(rows, r => r.Editable);
    }

    [Theory]
    [InlineData("SherpaOnnx", 1, "Voxtral")]
    [InlineData("Voxtral", 1, "Disabled")]
    [InlineData("Disabled", 1, "SherpaOnnx")]
    [InlineData("SherpaOnnx", -1, "Disabled")]
    [InlineData("unknown", 1, "Voxtral")]
    public void NextOption_CyclesAndWraps(string current, int direction, string expected)
    {
        var row = new SettingRow("k", "engine", current, Editable: true, Options: SettingsModel.EngineOptions);

        Assert.Equal(expected, SettingsModel.NextOption(row, direction));
    }

    [Fact]
    public void ToUpdate_MapsKeysToPatchPayload()
    {
        Assert.Equal("Voxtral", SettingsModel.ToUpdate(SettingsModel.KeyNotificationEngine, "Voxtral")!.NotificationEngine);
        Assert.Equal(800, SettingsModel.ToUpdate(SettingsModel.KeyMaxResponseChars, "800")!.MaxResponseChars);
        Assert.Null(SettingsModel.ToUpdate(SettingsModel.KeyMaxResponseChars, "not-a-number"));
        Assert.Null(SettingsModel.ToUpdate(SettingsModel.KeyMaxResponseChars, "-5"));
        Assert.Null(SettingsModel.ToUpdate("unknown.key", "x"));
    }

    [Fact]
    public void StoreSetConfig_RaisesChanged_AndIgnoresNull()
    {
        var store = new TuiStateStore();
        var changes = 0;
        store.Changed += () => changes++;

        store.SetConfig(SampleConfig());
        Assert.Equal(1, changes);
        Assert.Equal("Ctrl+Shift+Q", store.Config!.Hotkeys.LeaderKey);

        store.SetConfig(null);
        Assert.Equal(1, changes);
        Assert.NotNull(store.Config);
    }
}
