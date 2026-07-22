using Tempest.Pipeline;

namespace Tempest.RazorParser;

/// <summary>The Razor engine or Roslyn failed unexpectedly while reading a document — a
/// bug in this frontend or its dependencies, never in the user's code (both tools
/// recover from arbitrary user text; anything judgeable is entry data instead). Carries
/// the component name because spans are position-only and the parser knows no paths; the
/// shell maps it back to a file. The exception is flattened to strings so the error
/// stays plain equatable data.</summary>
public sealed record RazorEngineError(string ComponentName, string ExceptionType, string Message) : IError
{
    public string Code => "RZP002";
}
