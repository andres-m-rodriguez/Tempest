namespace Tempest.Pipeline;

/// <summary>Where an element sits inside its source text — the neutral counterpart of
/// Roslyn's Location, so the pipeline stays dependency-free. No stage knows file paths;
/// the host that handed the source in owns that association and maps spans back onto
/// real locations. Offsets are UTF-16 character positions; lines and columns are
/// zero-based, matching Roslyn's LinePosition.</summary>
public readonly record struct SourceSpan(
    int Start,
    int Length,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    public static SourceSpan None { get; } = new(-1, 0, 0, 0, 0, 0);

    public bool IsNone => Start < 0;
}
