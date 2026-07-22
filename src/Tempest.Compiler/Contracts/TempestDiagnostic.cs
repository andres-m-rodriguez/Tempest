using Tempest.Pipeline;

namespace Tempest.Compiler;

/// <summary>A problem a shape rule found in the user's code, as plain data. The shell
/// maps these onto real Roslyn diagnostics; tests assert on them directly. Distinct
/// from <see cref="IError"/>: a diagnostic judges the user's code and never fails the
/// compile.</summary>
public sealed record TempestDiagnostic(
    string Id,
    string Title,
    string Message,
    /// <summary>Which component the judged member belongs to — the identity the shell
    /// uses to map the position-only <see cref="Span"/> back onto a real file.</summary>
    string ComponentName,
    TempestDiagnosticSeverity Severity,
    SourceSpan Span);
