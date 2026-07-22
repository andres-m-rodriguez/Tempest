using Tempest.Parsing;

namespace Tempest.RazorParser;

/// <summary>One memoized parse: the source (compared by value) and the document it
/// yielded. Only successes are cached — a failed parse retries on the next call.</summary>
internal sealed record CachedParse(RazorSource Source, TempestDocument Document);
