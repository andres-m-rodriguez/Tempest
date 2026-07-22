using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.Generators;

/// <summary>One source's parse — razor file or C# class — with the real path only this
/// shell knows: the association the pipeline deliberately never learns. Plain values,
/// so the incremental pipeline caches the parse stage by equality.</summary>
internal sealed record SourceParse(string Path, Result<TempestDocument> Result);
