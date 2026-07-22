namespace Tempest.Pipeline;

/// <summary>A pipeline failure as plain data — no pipeline stage throws across a
/// boundary; fallible calls return a <see cref="Result{T}"/> carrying one of these
/// instead. Implementations are records of strings and value types only, so an error
/// cache-compares by value like every other contract. Lives in this shared pipeline
/// library rather than Tempest.Abstract because Abstract is the runtime package users
/// reference; error plumbing is pipeline-internal and common to every stage.</summary>
public interface IError
{
    /// <summary>Stable machine-readable code, prefixed per library (e.g. "RZP001" for
    /// the razor parser).</summary>
    string Code { get; }

    /// <summary>Human-readable description of what broke, self-contained enough for the
    /// shell to report without knowing the error's concrete type.</summary>
    string Message { get; }
}
