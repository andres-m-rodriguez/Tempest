namespace Tempest.RazorParser;

/// <summary>One region of a razor document as (start, end) indexes into the source —
/// nothing is allocated until a consumer slices it.</summary>
internal readonly record struct RazorToken(int Start, int End, RazorTokenType Type)
{
    /// <summary>Allocates this token's text from the source.</summary>
    public string ToSlice(ReadOnlySpan<char> source) => source.Slice(Start, End - Start).ToString();
}
