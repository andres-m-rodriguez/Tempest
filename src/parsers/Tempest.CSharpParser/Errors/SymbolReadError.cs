using Tempest.Pipeline;

namespace Tempest.CSharpParser;

/// <summary>Reading a component's symbols failed unexpectedly — a bug in this frontend
/// or Roslyn, never in the user's code (anything judgeable is entry data instead). The
/// exception is flattened to strings so the error stays plain equatable data.</summary>
public sealed record SymbolReadError(string ComponentName, string ExceptionType, string Message) : IError
{
    public string Code => "CSP002";
}
