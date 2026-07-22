namespace Tempest.RazorParser;

/// <summary>The tokenizer's output: the source it scanned and the tokens it found,
/// together, so the parser stage can slice token text without re-carrying the source. A
/// ref struct — it lives for one parse and never escapes to a field.</summary>
internal readonly ref struct TokenizedDocument(ReadOnlySpan<char> data, IReadOnlyList<RazorToken> tokens)
{
    public ReadOnlySpan<char> Data { get; } = data;
    public IReadOnlyList<RazorToken> Tokens { get; } = tokens;
}
