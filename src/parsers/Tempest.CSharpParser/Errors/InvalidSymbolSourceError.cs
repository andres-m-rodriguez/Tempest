using Microsoft.CodeAnalysis;
using Tempest.Pipeline;

namespace Tempest.CSharpParser;

/// <summary>A symbol input breaking its contract — a shell bug, not a user-code
/// problem: a null symbol. Detected at the client boundary before any reading starts.</summary>
public sealed record InvalidSymbolSourceError(string Message) : IError
{
    public string Code => "CSP001";

    internal static InvalidSymbolSourceError? Check(INamedTypeSymbol? symbol)
        => symbol is null ? new("The component type symbol must not be null.") : null;
}
