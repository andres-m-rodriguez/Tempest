using Microsoft.CodeAnalysis;
using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.CSharpParser;

/// <summary>The facts entries carry, read off symbols: return classification,
/// accessibility text, spans, host resolution, and the PascalCase twin. Symbols resolve
/// types for real, so type names come out fully qualified and entries never need
/// ambient usings.</summary>
internal sealed class SymbolFactsService
{
    internal (ReturnKind Kind, string? ResultType) ClassifyReturn(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
            return (ReturnKind.Void, null);

        var returnType = method.ReturnType;
        return returnType.OriginalDefinition.ToDisplayString() switch
        {
            "System.Threading.Tasks.Task" => (ReturnKind.Task, null),
            "System.Threading.Tasks.Task<TResult>" => (ReturnKind.TaskOfT, TypeArgument(returnType)),
            "System.Threading.Tasks.ValueTask" => (ReturnKind.ValueTask, null),
            "System.Threading.Tasks.ValueTask<TResult>" => (ReturnKind.ValueTaskOfT, TypeArgument(returnType)),
            _ => (ReturnKind.Sync, Qualified(returnType)),
        };
    }

    internal string Qualified(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    internal string TypeArgument(ITypeSymbol type)
        => Qualified(((INamedTypeSymbol)type).TypeArguments[0]);

    internal string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "private",
    };

    internal string Namespace(INamedTypeSymbol component)
        => component.ContainingNamespace.IsGlobalNamespace
            ? ""
            : component.ContainingNamespace.ToDisplayString();

    /// <summary>Which Tempest host base the component inherits, walked up the real base
    /// chain by simple name — a symbol frontend sees indirect bases too. None is the
    /// TEM002 case, kept so the compiler diagnoses instead of the parser dropping.</summary>
    internal HostKind ResolveHost(INamedTypeSymbol component)
    {
        for (var type = component.BaseType; type is not null; type = type.BaseType)
        {
            var host = type.Name switch
            {
                "StatefulComponent" => HostKind.Component,
                "StatefulLayoutComponent" => HostKind.LayoutComponent,
                "StatefulControl" => HostKind.Control,
                "StatefulStore" => HostKind.Store,
                _ => HostKind.None,
            };
            if (host != HostKind.None)
                return host;
        }
        return HostKind.None;
    }

    internal SourceSpan Span(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null)
            return SourceSpan.None;

        var span = location.SourceSpan;
        var lines = location.GetLineSpan();
        return new SourceSpan(
            span.Start, span.Length,
            lines.StartLinePosition.Line, lines.StartLinePosition.Character,
            lines.EndLinePosition.Line, lines.EndLinePosition.Character);
    }

    /// <summary>The PascalCase twin a [Reactive] field's state property is named after.</summary>
    internal string ToPascal(string fieldName)
    {
        var trimmed = fieldName.TrimStart('_');
        if (trimmed.Length == 0)
            return "";
        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }
}
