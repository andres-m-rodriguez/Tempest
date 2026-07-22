namespace Tempest.Parsing;

/// <summary>How a resolver answered a component name. External is real-but-not-ours —
/// a component that exists (a package, another compilation) but is not this pipeline's
/// to parse — so callers skip it silently, where NotFound is worth a diagnostic.</summary>
public enum ResolveResult
{
    Resolved,
    NotFound,
    External,
}
