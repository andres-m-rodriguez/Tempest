using Tempest.Model;
using Tempest.Model.Entry;

namespace Tempest.Compiler;

/// <summary>The boundary between the two data worlds: takes what came in (raw
/// <see cref="SourceEntries"/> from any frontend) and produces what the tool uses
/// internally (validated <see cref="ComponentModel"/>s), reporting every shape-rule
/// failure as a <see cref="DiagnosticModel"/> on the way through.</summary>
public sealed class TempestCompiler
{
    /// <param name="sources">The entries each parsed source contributed.</param>
    /// <param name="importsUsings">'\n'-joined @using blocks of every _Imports.razor, so
    /// razor-sourced type names (emitted as written) resolve in the generated files.</param>
    public Compilation Compile(
        IEnumerable<SourceEntries> sources,
        IEnumerable<string>? importsUsings = null)
    {
        // A .cs method with [Event, Command] is found by both symbol providers; keep one copy.
        var methods = sources.SelectMany(s => s.Methods)
            .GroupBy(m => (m.Namespace, m.ComponentName, m.MethodName, m.ParamType))
            .Select(g => g.First())
            .ToList();

        var reactives = sources.SelectMany(s => s.Reactives)
            .GroupBy(r => (r.Namespace, r.ComponentName, r.FieldName))
            .Select(g => g.First())
            .ToList();

        var componentKeys = methods
            .Select(m => (m.Namespace, m.ComponentName))
            .Concat(reactives.Select(r => (r.Namespace, r.ComponentName)))
            .Distinct();

        var methodsByComponent = methods.ToLookup(m => (m.Namespace, m.ComponentName));
        var reactivesByComponent = reactives.ToLookup(r => (r.Namespace, r.ComponentName));

        var components = new List<ComponentModel>();
        var diagnostics = new List<DiagnosticModel>();

        foreach (var (ns, name) in componentKeys)
        {
            var validMethods = new List<EntryMethod>();
            var validReactives = new List<EntryReactive>();
            var componentOk = true;

            foreach (var m in methodsByComponent[(ns, name)])
            {
                if (!m.InheritsHostBase)
                {
                    diagnostics.Add(MustInheritHostBase(m.ComponentName, m.Span));
                    componentOk = false;
                    continue;
                }

                if (m.IsEvent &&
                    (m.ParameterCount != 1 ||
                     !m.ParamIsNestedRecordOfComponent ||
                     (m.HasCancellationToken && !m.IsCommand)))
                {
                    diagnostics.Add(EventHandlerShape(m.MethodName, m.Span));
                    continue;
                }

                if (m.IsCommand && !m.IsEvent && m.ParameterCount != 0)
                {
                    diagnostics.Add(CommandShape(m.MethodName, m.Span));
                    continue;
                }

                validMethods.Add(m);
            }

            foreach (var r in reactivesByComponent[(ns, name)])
            {
                if (!r.InheritsHostBase)
                {
                    diagnostics.Add(MustInheritHostBase(r.ComponentName, r.Span));
                    componentOk = false;
                    continue;
                }

                if (!r.IsValidField)
                {
                    diagnostics.Add(ReactiveFieldShape(r.FieldName, r.Span));
                    continue;
                }

                if (r.Hook is { IsPartial: false } lateHook)
                {
                    diagnostics.Add(HookNotPartial(lateHook.MethodName, r.FieldName, lateHook.Span));
                    validReactives.Add(r with { Hook = null });
                    continue;
                }

                validReactives.Add(r);
            }

            if (!componentOk || (validMethods.Count == 0 && validReactives.Count == 0))
                continue;

            // Razor-sourced members carry type names as written; give the generated file
            // the source file usings plus every _Imports.razor usings so they resolve.
            var usings = new SortedSet<string>(StringComparer.Ordinal);
            if (validMethods.Any(m => m.FromRazor) || validReactives.Any(r => r.FromRazor))
            {
                foreach (var joined in importsUsings ?? [])
                    AddUsings(usings, joined);
                foreach (var m in validMethods)
                    AddUsings(usings, m.FileUsings);
                foreach (var r in validReactives)
                    AddUsings(usings, r.FileUsings);
            }

            components.Add(new ComponentModel(
                Namespace: ns,
                Name: name,
                Methods: new EquatableArray<MethodModel>(validMethods.Select(ToModel).ToArray()),
                Reactives: new EquatableArray<ReactiveModel>(validReactives.Select(ToModel).ToArray()),
                Usings: new EquatableArray<string>([.. usings])));
        }

        return new Compilation(
            new EquatableArray<ComponentModel>(components.ToArray()),
            new EquatableArray<DiagnosticModel>(diagnostics.ToArray()));
    }

    private static MethodModel ToModel(EntryMethod m) => new(
        MethodName: m.MethodName,
        IsCommand: m.IsCommand,
        IsEvent: m.IsEvent,
        Kind: m.Kind,
        ResultType: m.ResultType,
        HasCancellationToken: m.HasCancellationToken,
        ParamType: m.ParamType,
        ParamTypeName: m.ParamTypeName,
        Accessibility: m.Accessibility);

    private static ReactiveModel ToModel(EntryReactive r) => new(
        FieldName: r.FieldName,
        PropertyName: r.PropertyName,
        TypeText: r.TypeText,
        Hook: r.Hook is null
            ? null
            : new HookModel(r.Hook.Accessibility, r.Hook.ReturnsTask, r.Hook.ParamName, r.Hook.MethodName),
        Accessibility: r.Accessibility);

    private static readonly HashSet<string> StandardUsings =
        ["System", "System.Collections.Generic", "System.Threading", "System.Threading.Tasks"];

    private static void AddUsings(SortedSet<string> target, string joined)
    {
        foreach (var directive in joined.Split('\n'))
        {
            if (directive.Length > 0 && !StandardUsings.Contains(directive))
                target.Add(directive);
        }
    }

    // -- The shape rules, as data ---------------------------------------------

    private static DiagnosticModel EventHandlerShape(string method, SourceSpan span) => new(
        Id: "TEM001",
        Title: "[Event] handler has the wrong shape",
        Message: $"[Event] handler '{method}' must take exactly one parameter: a nested record of this component (an [Event, Command] may add a trailing CancellationToken)",
        Severity: Severity.Error,
        Span: span);

    private static DiagnosticModel MustInheritHostBase(string component, SourceSpan span) => new(
        Id: "TEM002",
        Title: "Component must inherit StatefulComponent",
        Message: $"'{component}' uses [Command], [Event] or [Reactive] but does not inherit Tempest.StatefulComponent (or Tempest.StatefulLayoutComponent for layouts)",
        Severity: Severity.Error,
        Span: span);

    private static DiagnosticModel CommandShape(string method, SourceSpan span) => new(
        Id: "TEM003",
        Title: "[Command] method has the wrong shape",
        Message: $"[Command] method '{method}' must be parameterless or take only a trailing CancellationToken, unless it is also an [Event] handler",
        Severity: Severity.Error,
        Span: span);

    private static DiagnosticModel ReactiveFieldShape(string field, SourceSpan span) => new(
        Id: "TEM007",
        Title: "[Reactive] field has the wrong shape",
        Message: $"[Reactive] field '{field}' must be a non-public, non-static field whose name differs from its PascalCase twin",
        Severity: Severity.Error,
        Span: span);

    private static DiagnosticModel HookNotPartial(string method, string field, SourceSpan span) => new(
        Id: "TEM008",
        Title: "Reactive hook is not partial",
        Message: $"Method '{method}' matches [Reactive] field '{field}' but is not partial; declare it 'partial' to wire it as the change hook",
        Severity: Severity.Warning,
        Span: span);
}
