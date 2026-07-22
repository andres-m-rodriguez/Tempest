namespace Tempest.RazorParser;

/// <summary>The tokenizer stage: one pass over the raw document indexing where razor
/// features live — line-start @directives, @code blocks, plain markup — as (start, end)
/// tokens into the source. A @code block's extent is found with a C#-aware brace scan
/// that skips strings, verbatim strings, char literals, and comments, so a brace hidden
/// in any literal never ends the block. Interpolated strings are skipped as string text;
/// their format holes are balanced in well-formed code, so block extents stay correct.
/// "@@" escapes to a literal '@' and tokenizes as text.
///
/// Helpers follow the capability rule: taking the <see cref="CharTracker"/> by ref means
/// a method may reposition the scan; taking only the span means it may only look.
/// Nothing here allocates text — tokens are indexes until someone slices.</summary>
internal sealed class RazorTokenizer
{
    internal TokenizedDocument Tokenize(ReadOnlySpan<char> data)
    {
        var tokens = new List<RazorToken>();
        var tracker = new CharTracker(data);
        var textStart = 0;

        while (tracker.IsValid)
        {
            if (!tracker.AtLineStart)
            {
                tracker.Advance();
                continue;
            }

            if (TrySkipEscapedAt(ref tracker, data))
                continue;   // "@@" renders one '@' — stays text

            if (TryScanCodeBlock(ref tracker, data, out var codeBlock))
            {
                FlushText(tokens, textStart, codeBlock.Start);
                tokens.Add(codeBlock);
                textStart = tracker.Position;
                continue;
            }

            if (TryScanDirective(ref tracker, data, out var directive))
            {
                FlushText(tokens, textStart, directive.Start);
                tokens.Add(directive);
                textStart = tracker.Position;
                continue;
            }

            tracker.Advance();
        }

        FlushText(tokens, textStart, data.Length);
        return new TokenizedDocument(data, tokens);
    }

    private static bool TrySkipEscapedAt(ref CharTracker tracker, ReadOnlySpan<char> data)
    {
        if (DirectiveStart(data, tracker.Position) is not { } at)
            return false;
        if (at + 1 >= data.Length || data[at + 1] != '@')
            return false;

        tracker.JumpTo(at + 2);
        return true;
    }

    private static bool TryScanCodeBlock(ref CharTracker tracker, ReadOnlySpan<char> data, out RazorToken token)
    {
        token = default;

        if (DirectiveStart(data, tracker.Position) is not { } at)
            return false;
        var wordEnd = WordEnd(data, at + 1);
        if (!IsCodeWord(data, at + 1, wordEnd))
            return false;
        if (BlockOpen(data, wordEnd) is not { } open || BlockClose(data, open) is not { } close)
            return false;

        token = new RazorToken(at, close + 1, RazorTokenType.CodeBlock);
        tracker.JumpTo(close + 1);
        return true;
    }

    private static bool TryScanDirective(ref CharTracker tracker, ReadOnlySpan<char> data, out RazorToken token)
    {
        token = default;

        if (DirectiveStart(data, tracker.Position) is not { } at)
            return false;
        var wordEnd = WordEnd(data, at + 1);
        if (wordEnd == at + 1)
            return false;   // bare '@' — markup

        var lineEnd = LineEnd(data, wordEnd);
        token = new RazorToken(at, lineEnd, RazorTokenType.Directive);
        tracker.JumpTo(lineEnd);
        return true;
    }

    /// <summary>The index of a '@' reachable from a line start over spaces/tabs, else null.</summary>
    private static int? DirectiveStart(ReadOnlySpan<char> data, int lineStart)
    {
        var i = lineStart;
        while (i < data.Length && data[i] is ' ' or '\t')
            i++;
        return i < data.Length && data[i] == '@' ? i : null;
    }

    private static int WordEnd(ReadOnlySpan<char> data, int start)
    {
        var i = start;
        while (i < data.Length && char.IsLetter(data[i]))
            i++;
        return i;
    }

    private static bool IsCodeWord(ReadOnlySpan<char> data, int start, int end)
        => end - start == 4 && data.Slice(start, 4).SequenceEqual("code".AsSpan());

    /// <summary>The '{' opening a @code block, over any whitespace, else null.</summary>
    private static int? BlockOpen(ReadOnlySpan<char> data, int afterWord)
    {
        var i = afterWord;
        while (i < data.Length && char.IsWhiteSpace(data[i]))
            i++;
        return i < data.Length && data[i] == '{' ? i : null;
    }

    /// <summary>The matching '}' of a block opened at <paramref name="open"/>, skipping
    /// every C# construct a stray brace can hide in; null when unterminated.</summary>
    private static int? BlockClose(ReadOnlySpan<char> data, int open)
    {
        var depth = 1;
        var i = open + 1;
        while (i < data.Length)
        {
            var c = data[i];
            if (c == '/' && i + 1 < data.Length && data[i + 1] == '/')
            {
                i = LineEnd(data, i);
            }
            else if (c == '/' && i + 1 < data.Length && data[i + 1] == '*')
            {
                i = SkipBlockComment(data, i + 2);
            }
            else if (c == '\'')
            {
                i = SkipCharLiteral(data, i + 1);
            }
            else if (c == '@' && i + 1 < data.Length && data[i + 1] == '"')
            {
                i = SkipVerbatimString(data, i + 2);
            }
            else if (c is '@' or '$' && i + 2 < data.Length && data[i + 2] == '"' &&
                     ((c == '@' && data[i + 1] == '$') || (c == '$' && data[i + 1] == '@')))
            {
                i = SkipVerbatimString(data, i + 3);
            }
            else if (c == '$' && i + 1 < data.Length && data[i + 1] == '"')
            {
                i = SkipString(data, i + 2);
            }
            else if (c == '"')
            {
                i = SkipString(data, i + 1);
            }
            else
            {
                if (c == '{')
                    depth++;
                else if (c == '}' && --depth == 0)
                    return i;
                i++;
            }
        }
        return null;
    }

    private static int LineEnd(ReadOnlySpan<char> data, int i)
    {
        while (i < data.Length && data[i] != '\n')
            i++;
        return i;
    }

    private static int SkipString(ReadOnlySpan<char> data, int i)
    {
        while (i < data.Length)
        {
            if (data[i] == '\\')
                i += 2;
            else if (data[i] == '"')
                return i + 1;
            else
                i++;
        }
        return i;
    }

    private static int SkipVerbatimString(ReadOnlySpan<char> data, int i)
    {
        while (i < data.Length)
        {
            if (data[i] != '"')
                i++;
            else if (i + 1 < data.Length && data[i + 1] == '"')
                i += 2;   // "" escapes a quote inside verbatim text
            else
                return i + 1;
        }
        return i;
    }

    private static int SkipCharLiteral(ReadOnlySpan<char> data, int i)
    {
        if (i < data.Length && data[i] == '\\')
            i += 2;
        else
            i++;
        if (i < data.Length && data[i] == '\'')
            i++;
        return i;
    }

    private static int SkipBlockComment(ReadOnlySpan<char> data, int i)
    {
        while (i + 1 < data.Length)
        {
            if (data[i] == '*' && data[i + 1] == '/')
                return i + 2;
            i++;
        }
        return data.Length;
    }

    private static void FlushText(List<RazorToken> tokens, int start, int end)
    {
        if (end > start)
            tokens.Add(new RazorToken(start, end, RazorTokenType.Text));
    }

    /// <summary>Position over the raw document, owned by the tokenizer that scans with
    /// it. Capabilities are parameters: a helper that takes this tracker by ref may
    /// reposition the scan; one that takes only the span may only look.</summary>
    private ref struct CharTracker(ReadOnlySpan<char> data)
    {
        private readonly ReadOnlySpan<char> _data = data;
        private int _idx = 0;

        public readonly int Position => _idx;

        public readonly bool IsValid => _idx < _data.Length;

        public readonly bool AtLineStart => _idx == 0 || _data[_idx - 1] == '\n';

        public void Advance() => _idx++;

        /// <summary>Repositions past everything up to <paramref name="position"/>.</summary>
        public void JumpTo(int position) => _idx = position;
    }
}
