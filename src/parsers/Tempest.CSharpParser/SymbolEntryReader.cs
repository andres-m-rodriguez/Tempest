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
    private const string CanExecuteAttribute = "Tempest.CanExecuteAttribute";
    private const string RunOnLoadAttribute = "Tempest.RunOnLoadAttribute";

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
        var canExecutes = new List<SourceCanExecute>();

        foreach (var member in component.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    ReadMethod(component, ns, host, method, methods, hooks, canExecutes);
                    break;
                case IFieldSymbol field when HasAttribute(field, ReactiveAttribute):
                    reactives.Add(ReadReactive(component, ns, host, field));
                    break;
                case IPropertySymbol property when FindAttribute(property, CanExecuteAttribute) is { } gate:
                    canExecutes.Add(new SourceCanExecute(
                        Namespace: ns,
                        ComponentName: component.Name,
                        MemberName: property.Name,
                        ExplicitTarget: gate.ConstructorArguments.FirstOrDefault().Value as string,
                        ReturnsBool: property.Type.SpecialType == SpecialType.System_Boolean,
                        IsMethod: false,
                        ParameterCount: 0,
                        Span: _facts.Span(property)));
                    break;
            }
        }

        return new TempestDocument(
            Commands: new EquatableArray<SourceMethod>(methods.Where(m => m.IsCommand).ToArray()),
            Events: new EquatableArray<SourceMethod>(methods.Where(m => m.IsEvent).ToArray()),
            Reactives: new EquatableArray<SourceReactiveProperty>(reactives.ToArray()),
            Hooks: new EquatableArray<SourceHook>(hooks.ToArray()),
            CanExecutes: new EquatableArray<SourceCanExecute>(canExecutes.ToArray()),
            Injections: ReadInjection(component, ns, host) is { } injection
                ? new EquatableArray<SourceInjection>([injection])
                : EquatableArray<SourceInjection>.Empty);
    }

    /// <summary>The injection request: exactly one explicit constructor (primary or
    /// classic), taking parameters, with no parameterless alternative — anything else
    /// means the user is handling construction and the emitter must not add one.</summary>
    private SourceInjection? ReadInjection(INamedTypeSymbol component, string ns, HostKind host)
    {
        IMethodSymbol? explicitCtor = null;
        foreach (var ctor in component.InstanceConstructors)
        {
            if (ctor.IsImplicitlyDeclared)
                continue;
            if (ctor.Parameters.Length == 0 || explicitCtor is not null)
                return null;
            explicitCtor = ctor;
        }

        if (explicitCtor is null)
            return null;

        return new SourceInjection(
            Namespace: ns,
            ComponentName: component.Name,
            ParameterTypes: new EquatableArray<string>(
                explicitCtor.Parameters.Select(p => _facts.Qualified(p.Type)).ToArray()),
            Host: host,
            Span: _facts.Span(explicitCtor));
    }

    private void ReadMethod(
        INamedTypeSymbol component, string ns, HostKind host, IMethodSymbol method,
        List<SourceMethod> methods, List<SourceHook> hooks, List<SourceCanExecute> canExecutes)
    {
        var isCommand = false;
        var isEvent = false;
        var runOnLoad = false;
        AttributeData? onChanged = null;
        AttributeData? canExecute = null;
        foreach (var attribute in method.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case CommandAttribute: isCommand = true; break;
                case EventAttribute: isEvent = true; break;
                case RunOnLoadAttribute: runOnLoad = true; break;
                case OnChangedAttribute: onChanged ??= attribute; break;
                case CanExecuteAttribute: canExecute ??= attribute; break;
            }
        }

        if (canExecute is not null)
        {
            canExecutes.Add(new SourceCanExecute(
                Namespace: ns,
                ComponentName: component.Name,
                MemberName: method.Name,
                ExplicitTarget: canExecute.ConstructorArguments.FirstOrDefault().Value as string,
                ReturnsBool: method.ReturnType.SpecialType == SpecialType.System_Boolean,
                IsMethod: true,
                ParameterCount: method.Parameters.Length,
                Span: _facts.Span(method)));
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
            RunOnLoad: runOnLoad,
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

    private static AttributeData? FindAttribute(ISymbol symbol, string attributeName)
        => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeName);
}
