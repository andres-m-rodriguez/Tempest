namespace Tempest.Model;

/// <summary>Where a model element came from in its source file — the neutral counterpart
/// of Roslyn's Location, so the model stays dependency-free. Offsets are UTF-16 character
/// positions; lines and columns are zero-based, matching Roslyn's LinePosition.</summary>
public readonly record struct SourceSpan(
    string FilePath,
    int Start,
    int Length,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    /// <summary>An element with no usable source location (synthesized, or mapping failed).</summary>
    public static SourceSpan None { get; } = new("", 0, 0, 0, 0, 0, 0);

    public bool IsNone => FilePath.Length == 0;
}
