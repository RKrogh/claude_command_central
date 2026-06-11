using System.Text.RegularExpressions;

namespace CommandCentral.Output;

/// <summary>
/// Converts a markdown assistant response into text suitable for speech:
/// code blocks become a short spoken marker, markdown syntax is stripped,
/// and the result is truncated to a configurable length, preferring a
/// sentence boundary. Cloud TTS bills per character, so truncation is also
/// a cost guard.
/// </summary>
public static partial class ResponseSpeechSanitizer
{
    private const string CodeBlockMarker = " Code block omitted. ";
    private const string TruncationMarker = " Response truncated.";

    public static string Sanitize(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var s = FencedCodeBlock().Replace(text, CodeBlockMarker);
        s = MarkdownLink().Replace(s, "$1");
        s = InlineCode().Replace(s, "$1");
        s = HeadingMarker().Replace(s, "");
        s = BulletMarker().Replace(s, "");
        s = s.Replace("**", "").Replace("*", " ");
        s = ExcessBlankLines().Replace(s, "\n\n");
        s = s.Trim();

        if (maxChars > 0 && s.Length > maxChars)
            s = Truncate(s, maxChars);

        return s;
    }

    private static string Truncate(string s, int maxChars)
    {
        var window = s[..maxChars];

        // Prefer a sentence boundary; fall back to a word boundary when the
        // last sentence end would throw away more than half the window.
        var cut = window.LastIndexOfAny(['.', '!', '?']);
        if (cut < maxChars / 2)
            cut = window.LastIndexOf(' ');

        var head = (cut > 0 ? window[..(cut + 1)] : window).TrimEnd();
        if (head.Length > 0 && head[^1] is not ('.' or '!' or '?'))
            head += ".";

        return head + TruncationMarker;
    }

    [GeneratedRegex(@"```.*?(?:```|\z)", RegexOptions.Singleline)]
    private static partial Regex FencedCodeBlock();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"`([^`\n]*)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^#{1,6}\s*", RegexOptions.Multiline)]
    private static partial Regex HeadingMarker();

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+", RegexOptions.Multiline)]
    private static partial Regex BulletMarker();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLines();
}
