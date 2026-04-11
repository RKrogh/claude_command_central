using CommandCentral.Input;
using SharpHook.Data;

namespace CommandCentral.Integration.Tests;

public class KeyComboTests
{
    [Theory]
    [InlineData("Ctrl+1", EventMask.LeftCtrl, KeyCode.Vc1)]
    [InlineData("Ctrl+0", EventMask.LeftCtrl, KeyCode.Vc0)]
    [InlineData("Ctrl+Space", EventMask.LeftCtrl, KeyCode.VcSpace)]
    [InlineData("Ctrl+BackQuote", EventMask.LeftCtrl, KeyCode.VcBackQuote)]
    [InlineData("Ctrl+K", EventMask.LeftCtrl, KeyCode.VcK)]
    [InlineData("Ctrl+F5", EventMask.LeftCtrl, KeyCode.VcF5)]
    public void Parse_SingleModifier(string combo, EventMask expectedMod, KeyCode expectedKey)
    {
        var result = KeyCombo.Parse(combo);

        Assert.Equal(expectedMod, result.Modifiers);
        Assert.Equal(expectedKey, result.Key);
    }

    [Theory]
    [InlineData("Ctrl+Shift+K", EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.VcK)]
    [InlineData("Ctrl+Shift+1", EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.Vc1)]
    [InlineData("Alt+Shift+F12", EventMask.LeftAlt | EventMask.LeftShift, KeyCode.VcF12)]
    public void Parse_MultipleModifiers(string combo, EventMask expectedMod, KeyCode expectedKey)
    {
        var result = KeyCombo.Parse(combo);

        Assert.Equal(expectedMod, result.Modifiers);
        Assert.Equal(expectedKey, result.Key);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var result = KeyCombo.Parse("ctrl+shift+k");

        Assert.Equal(EventMask.LeftCtrl | EventMask.LeftShift, result.Modifiers);
        Assert.Equal(KeyCode.VcK, result.Key);
    }

    [Fact]
    public void Parse_ThrowsOnNoKey()
    {
        Assert.Throws<ArgumentException>(() => KeyCombo.Parse("Ctrl+Shift"));
    }

    [Fact]
    public void Parse_ThrowsOnUnknownKey()
    {
        Assert.Throws<ArgumentException>(() => KeyCombo.Parse("Ctrl+FooBar"));
    }

    [Fact]
    public void TryParse_ReturnsFalseOnInvalid()
    {
        Assert.False(KeyCombo.TryParse("Ctrl+???", out _));
    }

    [Fact]
    public void TryParse_ReturnsTrueOnValid()
    {
        Assert.True(KeyCombo.TryParse("Ctrl+K", out var combo));
        Assert.Equal(KeyCode.VcK, combo.Key);
    }

    [Fact]
    public void Matches_LeftCtrl()
    {
        var combo = KeyCombo.Parse("Ctrl+1");

        Assert.True(combo.Matches(EventMask.LeftCtrl, KeyCode.Vc1));
    }

    [Fact]
    public void Matches_RightCtrl()
    {
        var combo = KeyCombo.Parse("Ctrl+1");

        // HasCtrl() matches both left and right
        Assert.True(combo.Matches(EventMask.RightCtrl, KeyCode.Vc1));
    }

    [Fact]
    public void Matches_WrongKey_ReturnsFalse()
    {
        var combo = KeyCombo.Parse("Ctrl+1");

        Assert.False(combo.Matches(EventMask.LeftCtrl, KeyCode.Vc2));
    }

    [Fact]
    public void Matches_MissingModifier_ReturnsFalse()
    {
        var combo = KeyCombo.Parse("Ctrl+Shift+K");

        // Only Ctrl, no Shift
        Assert.False(combo.Matches(EventMask.LeftCtrl, KeyCode.VcK));
    }

    [Fact]
    public void Matches_ExtraModifiersRejected()
    {
        // Ctrl+1 must NOT match when Shift is also held (that's Ctrl+Shift+1)
        var combo = KeyCombo.Parse("Ctrl+1");

        Assert.False(combo.Matches(EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.Vc1));
    }

    [Fact]
    public void Matches_CtrlShiftCombo_RequiresBothModifiers()
    {
        var combo = KeyCombo.Parse("Ctrl+Shift+1");

        Assert.True(combo.Matches(EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.Vc1));
        Assert.False(combo.Matches(EventMask.LeftCtrl, KeyCode.Vc1));
        Assert.False(combo.Matches(EventMask.LeftShift, KeyCode.Vc1));
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        var combo = KeyCombo.Parse("Ctrl+Shift+K");
        var str = combo.ToString();

        Assert.Equal("Ctrl+Shift+K", str);
    }

    [Theory]
    [InlineData("`")]
    [InlineData("backtick")]
    [InlineData("backquote")]
    [InlineData("oemtilde")]
    public void Parse_BackQuoteAliases(string keyName)
    {
        var combo = KeyCombo.Parse($"Ctrl+{keyName}");

        Assert.Equal(KeyCode.VcBackQuote, combo.Key);
    }

    [Theory]
    [InlineData("section")]
    [InlineData("§")]
    [InlineData("102")]
    [InlineData("oem102")]
    public void Parse_SectionKeyAliases(string keyName)
    {
        var combo = KeyCombo.Parse(keyName);

        Assert.Equal(EventMask.None, combo.Modifiers);
        Assert.Equal(KeyCode.Vc102, combo.Key);
    }

    [Fact]
    public void Parse_LeaderKey_CtrlShiftSection()
    {
        var combo = KeyCombo.Parse("Ctrl+Shift+Section");

        Assert.Equal(EventMask.LeftCtrl | EventMask.LeftShift, combo.Modifiers);
        Assert.Equal(KeyCode.Vc102, combo.Key);
    }

    [Fact]
    public void Parse_NoModifier_SingleKey()
    {
        var combo = KeyCombo.Parse("1");

        Assert.Equal(EventMask.None, combo.Modifiers);
        Assert.Equal(KeyCode.Vc1, combo.Key);
    }

    [Fact]
    public void Matches_NoModifier_PlainKey()
    {
        var combo = KeyCombo.Parse("1");

        // Plain key with no modifiers held
        Assert.True(combo.Matches(EventMask.None, KeyCode.Vc1));
    }

    [Fact]
    public void Matches_NoModifier_RejectsExtraModifiers()
    {
        var combo = KeyCombo.Parse("1");

        // Plain "1" should NOT match when Ctrl is held
        Assert.False(combo.Matches(EventMask.LeftCtrl, KeyCode.Vc1));
    }
}
