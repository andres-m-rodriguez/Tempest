using System.Text.RegularExpressions;

namespace Tempest.RazorParser;

/// <summary>Character-level primitives for slicing C# out of razor text: the @code block
/// finder, the brace matcher and the string skipper it leans on, and the @using scraper.
/// These run on raw text because the Razor compiler's own output is invisible to other
/// generators.</summary>
public static class RazorText
{
    /// <summary>Finds each `@code { ... }` block; yields (content offset in file, content).
    /// An unterminated block ends the scan: nothing after it can be trusted.</summary>
    public static IEnumerable<(int Start, string Text)> ExtractCodeBlocks(string text)
    {
        var i = 0;
        while ((i = text.IndexOf("@code", i, StringComparison.Ordinal)) >= 0)
        {
            var after = i + "@code".Length;
            var boundedBefore = i == 0 ||
                (!char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_' && text[i - 1] != '@');

            var j = after;
            while (j < text.Length && char.IsWhiteSpace(text[j]))
                j++;

            if (!boundedBefore || j >= text.Length || text[j] != '{')
            {
                i = after;
                continue;
            }

            var close = FindMatchingBrace(text, j);
            if (close < 0)
                yield break;

            yield return (j + 1, text.Substring(j + 1, close - j - 1));
            i = close + 1;
        }
    }

    /// <summary>Brace matching that skips comments and string/char literals.</summary>
    public static int FindMatchingBrace(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    if (--depth == 0) return i;
                    break;
                case '/':
                    if (i + 1 < s.Length && s[i + 1] == '/')
                    {
                        i = s.IndexOf('\n', i);
                        if (i < 0) return -1;
                    }
                    else if (i + 1 < s.Length && s[i + 1] == '*')
                    {
                        i = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        if (i < 0) return -1;
                        i++;
                    }
                    break;
                case '"':
                {
                    var verbatim = i > open && (s[i - 1] == '@' ||
                        (s[i - 1] == '$' && i >= 2 && s[i - 2] == '@'));
                    i = SkipString(s, i, verbatim);
                    if (i < 0) return -1;
                    break;
                }
                case '\'':
                    i++;
                    if (i < s.Length && s[i] == '\\') i++;
                    i++;
                    break;
            }
        }
        return -1;
    }

    /// <summary>Returns the index of the closing quote, honoring \" escapes in ordinary
    /// strings and "" escapes in verbatim ones; -1 when the string never closes.</summary>
    public static int SkipString(string s, int openQuote, bool verbatim)
    {
        for (var i = openQuote + 1; i < s.Length; i++)
        {
            if (s[i] == '\\' && !verbatim)
            {
                i++;
            }
            else if (s[i] == '"')
            {
                if (verbatim && i + 1 < s.Length && s[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                return i;
            }
        }
        return -1;
    }

    /// <summary>All @using directives of a razor file, '\n'-joined in source order.</summary>
    public static string ExtractUsingDirectives(string text)
        => string.Join("\n", Regex.Matches(text, @"(?m)^\s*@using\s+([^\r\n]+)")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value.Trim().TrimEnd(';')));
}
