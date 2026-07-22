namespace Tempest.RazorParser;

/// <summary>One @code block. Start/End span the whole feature (@code through its
/// closing brace); ContentStart/ContentEnd bound the C# between the braces, which is
/// what Roslyn parses and what span math maps back from.</summary>
internal sealed record CodeBlockNode(int ContentStart, int ContentEnd, int Start, int End) : Node(Start, End)
{
    /// <summary>Allocates the C# between the braces.</summary>
    public string ContentSlice(ReadOnlySpan<char> source)
        => source.Slice(ContentStart, ContentEnd - ContentStart).ToString();
}
