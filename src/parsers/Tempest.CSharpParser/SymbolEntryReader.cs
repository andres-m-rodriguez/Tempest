using Microsoft.CodeAnalysis;
using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.CSharpParser;

/// <summary>The frontend's orchestrator: one pass over a component type's members —
/// every [Command]/[Event] method, [Reactive] field, and [OnChanged] hook, exactly as
/// declared, before any shape validation. Symbols resolve types for real, so entries
/// carry fully-qualified type names and never need ambient usings.</summary>
internal sealed class SymbolEntryReader
{
    private const string CommandAttribute = "Tempest.CommandAttribute";
    private const string EventAttribute = "Tempest.EventAttribute";
    private const string ReactiveAttribute = "Tempest.ReactiveAttribute";
    private const string OnChangedAttribute = "Tempest.OnChangedAttribute";

    private readonly SymbolFactsService _facts = new();

    internal Result<TempestDocument> Read(INamedTypeSymbol? component)
    {
        if (InvalidSymbolSourceError.Check(component) is { } invalid)
            return Result<TempestDocument>.Fail(invalid);

        try
        {
            return Result<TempestDocument>.Ok(ReadCore(component!));
        }
        catch (Exception ex)
        {
            return Result<TempestDocument>.Fail(
                new SymbolReadError(component!.Name, ex.GetType().Name, ex.Message));
        }
    }

    private TempestDocument ReadCore(INamedTypeSymbol component)
    {
        var ns = _facts.Namespace(component);
        var host = _facts.ResolveHost(component);

        var methods = new List<SourceMethod>();
        var reactives = new List<SourceReactiveProperty>();
        var hooks = new List<SourceHook>();

        foreach (var member in component.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    ReadMethod(component, ns, host, method, methods, hooks);
                    break;
                case IFieldSymbol field when HasAttribute(field, ReactiveAttribute):
                    reactives.Add(ReadReactive(component, ns, host, field));
                    break;
            }
        }

        return new TempestDocument(
            Commands: new EquatableArray<SourceMethod>(methods.Where(m => m.IsCommand).ToArray()),
            Events: new EquatableArray<SourceMethod>(methods.Where(m => m.IsEvent).ToArray()),
            Reactives: new EquatableArray<SourceReactiveProperty>(reactives.ToArray()),
            Hooks: new EquatableArray<SourceHook>(hooks.ToArray()));
    }

    private void ReadMethod(
        INamedTypeSymbol component, string ns, HostKind host, IMethodSymbol method,
        List<SourceMethod> methods, List<SourceHook> hooks)
    {
        var isCommand = false;
        var isEvent = false;
        AttributeData? onChanged = null;
        foreach (var attribute in method.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case CommandAttribute: isCommand = true; break;
                case EventAttribute: isEvent = true; break;
                case OnChangedAttribute: onChanged ??= attribute; break;
            }
        }

        if (onChanged is not null)
        {
            hooks.Add(new SourceHook(
                Namespace: ns,
                ComponentName: component.Name,
                MethodName: method.Name,
                ExplicitTarget: onChanged.ConstructorArguments.FirstOrDefault().Value as string,
                ReturnsTask: !method.ReturnsVoid,
                ParamName: method.Parameters.FirstOrDefault()?.Name ?? "",
                ParameterCount: method.Parameters.Length,
                Accessibility: _facts.AccessibilityText(method.DeclaredAccessibility),
                Span: _facts.Span(method)));
        }

        if (!isCommand && !isEvent)
            return;

        var (kind, resultType) = _facts.ClassifyReturn(method);

        var parameters = method.Parameters;
        var hasCt = parameters.Length > 0 &&
                    parameters[parameters.Length - 1].Type.ToDisplayString() == "System.Threading.CancellationToken";
        var effective = hasCt ? parameters.Length - 1 : parameters.Length;

        string? paramType = null;
        string? paramTypeName = null;
        var paramIsNestedRecord = false;
        if (effective == 1 && parameters[0].Type is INamedTypeSymbol parameterType)
        {
            paramType = _facts.Qualified(parameterType);
            paramTypeName = parameterType.Name;
            paramIsNestedRecord = parameterType.IsRecord &&
                SymbolEqualityComparer.Default.Equals(parameterType.ContainingType, component);
        }

        methods.Add(new SourceMethod(
            Namespace: ns,
            ComponentName: component.Name,
            MethodName: method.Name,
            IsCommand: isCommand,
            IsEvent: isEvent,
            Kind: kind,
            ResultType: resultType,
            HasCancellationToken: hasCt,
            ParameterCount: effective,
            ParamType: paramType,
            ParamTypeName: paramTypeName,
            ParamIsNestedRecordOfComponent: paramIsNestedRecord,
            Host: host,
            Accessibility: _facts.AccessibilityText(method.DeclaredAccessibility),
            NeedsAmbientUsings: false,
            FileUsings: "",
            Span: _facts.Span(method)));
    }

    private SourceReactiveProperty ReadReactive(
        INamedTypeSymbol component, string ns, HostKind host, IFieldSymbol field)
    {
        var propertyName = _facts.ToPascal(field.Name);
        return new SourceReactiveProperty(
            Namespace: ns,
            ComponentName: component.Name,
            FieldName: field.Name,
            PropertyName: propertyName,
            TypeText: _facts.Qualified(field.Type),
            Host: host,
            IsValidField: field.DeclaredAccessibility != Accessibility.Public &&
                          !field.IsStatic &&
                          propertyName != field.Name &&
                          propertyName.Length > 0,
            Accessibility: _facts.AccessibilityText(field.DeclaredAccessibility),
            NeedsAmbientUsings: false,
            FileUsings: "",
            Span: _facts.Span(field));
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeName);
}
