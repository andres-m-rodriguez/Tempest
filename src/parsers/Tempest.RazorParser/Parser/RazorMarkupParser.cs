namespace Tempest.RazorParser;

/// <summary>The parser stage: walks the tokenizer's output with a
/// <see cref="TokenTracker"/> and assembles nodes — directives split into name and
/// value, code blocks located by their content span. Markup text carries nothing this
/// pipeline needs, so it yields no node.
///
/// Helpers follow the capability rule: taking the <see cref="TokenizedDocument"/> means
/// a method may allocate slices of the source; taking only the span means it may only
/// look.</summary>
internal sealed class RazorMarkupParser
{
    internal IReadOnlyList<Node> Parse(TokenizedDocument document)
    {
        var nodes = new List<Node>();
        var tracker = new TokenTracker(document.Tokens);

        while (tracker.Advance(out var token))
        {
            switch (token.Type)
            {
                case RazorTokenType.Directive when TryParseDirective(document, token, out var directive):
                    nodes.Add(directive);
                    break;
                case RazorTokenType.CodeBlock when TryParseCodeBlock(document.Data, token, out var block):
                    nodes.Add(block);
                    break;
            }
        }

        return nodes;
    }

    /// <summary>Takes the document: splitting a directive allocates its name and value.</summary>
    private static bool TryParseDirective(TokenizedDocument document, RazorToken token, out DirectiveNode directive)
    {
        directive = null!;
        var data = document.Data;

        var i = token.Start + 1;   // past '@'
        var nameStart = i;
        while (i < token.End && char.IsLetter(data[i]))
            i++;
        if (i == nameStart)
            return false;

        var name = data.Slice(nameStart, i - nameStart).ToString();
        var value = data.Slice(i, token.End - i).Trim().ToString();
        directive = new DirectiveNode(name, value, token.Start, token.End);
        return true;
    }

    /// <summary>Takes only the span: locating the content bounds allocates nothing.</summary>
    private static bool TryParseCodeBlock(ReadOnlySpan<char> data, RazorToken token, out CodeBlockNode block)
    {
        block = null!;

        var open = token.Start;
        while (open < token.End && data[open] != '{')
            open++;
        if (open >= token.End)
            return false;

        block = new CodeBlockNode(open + 1, token.End - 1, token.Start, token.End);
        return true;
    }

    /// <summary>Position over the token list, owned by the parser that walks with it.
    /// Same capability rule as the tokenizer's CharTracker: a method handed this
    /// tracker may advance through the tokens; the list itself never moves.</summary>
    private struct TokenTracker(IReadOnlyList<RazorToken> tokens)
    {
        private readonly IReadOnlyList<RazorToken> _tokens = tokens;
        private int _idx = -1;

        public bool Advance(out RazorToken token)
        {
            var nextIdx = _idx + 1;
            if (nextIdx >= _tokens.Count)
            {
                token = default;
                return false;
            }

            _idx = nextIdx;
            token = _tokens[nextIdx];
            return true;
        }

        public readonly bool TryPeek(out RazorToken token)
        {
            var peekIdx = _idx + 1;
            if (peekIdx >= _tokens.Count)
            {
                token = default;
                return false;
            }

            token = _tokens[peekIdx];
            return true;
        }
    }
}
