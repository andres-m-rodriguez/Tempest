namespace Tempest.Pipeline;

/// <summary>The side-channel a pipeline stage reports through without failing: the host
/// injects an implementation, stages call it, and the call's <see cref="Result{T}"/>
/// stays about the value. Anything that must fail the call is an <see cref="IError"/>
/// instead.</summary>
public interface IDiagnostics
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

/// <summary>The default collector: accumulates diagnostics in order for the host to
/// drain after a run.</summary>
public sealed class Diagnostics : IDiagnostics
{
    private readonly List<Diagnostic> _items = [];

    public IReadOnlyList<Diagnostic> Items => _items;

    public void Info(string message) => _items.Add(new Diagnostic(TempestDiagnosticSeverity.Info, message));

    public void Warning(string message) => _items.Add(new Diagnostic(TempestDiagnosticSeverity.Warning, message));

    public void Error(string message) => _items.Add(new Diagnostic(TempestDiagnosticSeverity.Error, message));
}
