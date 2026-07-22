using System.Collections.Concurrent;
using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.RazorParser;

/// <summary>Memoizes one successful parse per document, keyed by component name — so
/// interleaved calls across many files each parse once. The name only picks the slot;
/// the stored source's value equality decides the hit, so an edited document misses and
/// overwrites its slot rather than returning stale entries. A failed parse is never
/// cached — the next call retries it. Concurrent because generator hosts parse in
/// parallel.</summary>
internal sealed class ParseCacheService
{
    private readonly ConcurrentDictionary<string, CachedParse> _parses = new();

    /// <summary>The memoized document, when the slot for this component holds a source
    /// value-equal to <paramref name="source"/>.</summary>
    internal bool TryGet(RazorSource? source, out Result<TempestDocument> entries)
    {
        if (source is { ComponentName: { } name } &&
            _parses.TryGetValue(name, out var hit) &&
            hit.Source == source)
        {
            entries = Result<TempestDocument>.Ok(hit.Document);
            return true;
        }

        entries = default;
        return false;
    }

    /// <summary>Publishes a successful parse into its component's slot; failures and
    /// sources too broken to name are never cached.</summary>
    internal void Store(RazorSource? source, Result<TempestDocument> entries)
    {
        if (entries.IsSuccess && source is { ComponentName: { } name })
            _parses[name] = new CachedParse(source, entries.Value);
    }
}
