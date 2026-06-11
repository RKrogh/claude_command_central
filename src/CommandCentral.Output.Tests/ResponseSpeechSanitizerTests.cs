namespace CommandCentral.Output.Tests;

public class ResponseSpeechSanitizerTests
{
    [Fact]
    public void Sanitize_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ResponseSpeechSanitizer.Sanitize("", 1500));
        Assert.Equal(string.Empty, ResponseSpeechSanitizer.Sanitize("   \n  ", 1500));
    }

    [Fact]
    public void Sanitize_PlainText_PassesThrough()
    {
        var result = ResponseSpeechSanitizer.Sanitize("The tests pass. All good.", 1500);

        Assert.Equal("The tests pass. All good.", result);
    }

    [Fact]
    public void Sanitize_FencedCodeBlock_ReplacedWithSpokenMarker()
    {
        var text = "Here is the fix:\n```csharp\nvar x = 1;\nvar y = 2;\n```\nDone.";

        var result = ResponseSpeechSanitizer.Sanitize(text, 1500);

        Assert.Contains("Code block omitted", result);
        Assert.DoesNotContain("var x", result);
        Assert.Contains("Done.", result);
    }

    [Fact]
    public void Sanitize_UnterminatedCodeBlock_StillStripped()
    {
        var text = "Look:\n```\nnever closed";

        var result = ResponseSpeechSanitizer.Sanitize(text, 1500);

        Assert.DoesNotContain("never closed", result);
        Assert.Contains("Code block omitted", result);
    }

    [Fact]
    public void Sanitize_InlineCode_KeepsContentWithoutBackticks()
    {
        var result = ResponseSpeechSanitizer.Sanitize("Run `dotnet build` first.", 1500);

        Assert.Equal("Run dotnet build first.", result);
    }

    [Fact]
    public void Sanitize_MarkdownLink_KeepsLinkText()
    {
        var result = ResponseSpeechSanitizer.Sanitize("See [the docs](https://example.com/x) here.", 1500);

        Assert.Equal("See the docs here.", result);
    }

    [Fact]
    public void Sanitize_HeadingsBulletsAndEmphasis_Stripped()
    {
        var text = "## Summary\n- **First** point\n- Second *point*";

        var result = ResponseSpeechSanitizer.Sanitize(text, 1500);

        Assert.DoesNotContain("#", result);
        Assert.DoesNotContain("*", result);
        Assert.StartsWith("Summary", result);
        Assert.Contains("First point", result);
    }

    [Fact]
    public void Sanitize_LongText_TruncatesOnSentenceBoundary()
    {
        var sentence = "This is a complete sentence. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 100));

        var result = ResponseSpeechSanitizer.Sanitize(text, 200);

        Assert.True(result.Length <= 200 + " Response truncated.".Length);
        Assert.EndsWith("Response truncated.", result);
        // Cut on the sentence boundary, not mid-word
        Assert.DoesNotContain("sentenc Response", result);
    }

    [Fact]
    public void Sanitize_ZeroMaxChars_DoesNotTruncate()
    {
        var text = new string('a', 5000) + ".";

        var result = ResponseSpeechSanitizer.Sanitize(text, 0);

        Assert.Equal(5001, result.Length);
        Assert.DoesNotContain("truncated", result);
    }

    [Fact]
    public void Sanitize_TruncationWithoutSentenceEnd_CutsAtWordAndAddsPeriod()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 200));

        var result = ResponseSpeechSanitizer.Sanitize(text, 100);

        Assert.EndsWith(". Response truncated.", result);
        Assert.DoesNotContain("wor.", result);
    }

    [Fact]
    public void Sanitize_OnlyCodeBlockAndTrim_LeavesMarkerOnly()
    {
        var result = ResponseSpeechSanitizer.Sanitize("```\nx\n```", 1500);

        Assert.Equal("Code block omitted.", result);
    }
}
