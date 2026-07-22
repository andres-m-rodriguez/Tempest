namespace Tempest.Pipeline;

public enum TempestDiagnosticSeverity { Info, Warning, Error }

/// <summary>One reported condition as plain data. Distinct from <see cref="IError"/>:
/// an error fails the call that returned it, a diagnostic is collected on the side and
/// the call still succeeds.</summary>
public sealed record Diagnostic(TempestDiagnosticSeverity Severity, string Message);
