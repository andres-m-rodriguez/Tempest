namespace Tempest.RazorParser;

/// <summary>A parsed razor feature located in the source — position-only, like every
/// span in this pipeline. Text is allocated only where a consumer slices it.</summary>
internal abstract record Node(int Start, int End)
{
    /// <summary>Allocates this node's text from the source.</summary>
    public string ToSlice(ReadOnlySpan<char> source) => source.Slice(Start, End - Start).ToString();
}
