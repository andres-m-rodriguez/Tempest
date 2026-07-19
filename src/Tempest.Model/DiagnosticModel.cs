namespace Tempest.Model;

public enum Severity
{
    Hidden,
    Info,
    Warning,
    Error,
}

/// <summary>A problem a pipeline stage found, as data. The shell (the Roslyn generator)
/// maps these onto real Roslyn diagnostics; tests assert on them directly.</summary>
public sealed record DiagnosticModel(
    string Id,
    string Title,
    string Message,
    Severity Severity,
    SourceSpan Span);
