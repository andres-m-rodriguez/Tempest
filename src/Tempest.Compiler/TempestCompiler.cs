using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.Compiler;

/// <summary>The boundary between the two data worlds — the exposed surface of this
/// library. Takes the documents every frontend parsed and produces what the emitter
/// consumes: validated components, plus a <see cref="TempestDiagnostic"/> for every
/// member that failed a shape rule. User-code judgments never fail the call; the only
/// error is input breaking its own contract.</summary>
public sealed class TempestCompiler
{
    private readonly ComponentGroupingService _grouping = new();
    private readonly ShapeRuleService _rules = new();
    private readonly HookResolutionService _hooks = new();
    private readonly CanExecuteResolutionService _canExecutes = new();
    private readonly UsingsService _usings = new();

    /// <param name="documents">The parsed document of every source.</param>
    /// <param name="ambientUsings">'\n'-joined @using blocks of every _Imports.razor,
    /// so razor-sourced type names (carried as written) resolve in generated files.</param>
    public Result<TempestCompilation> Compile(
        IEnumerable<TempestDocument>? documents,
        IEnumerable<string>? ambientUsings = null)
    {
        if (InvalidCompileInputError.Check(documents, out var validated) is { } invalid)
            return Result<TempestCompilation>.Fail(invalid);

        var ambient = ambientUsings?.ToList() ?? [];
        var components = new List<CompiledComponent>();
        var diagnostics = new List<TempestDiagnostic>();

        foreach (var bucket in _grouping.Group(validated))
        {
            var methods = new List<SourceMethod>();
            var reactives = new List<SourceReactiveProperty>();
            var hosted = true;
            var host = HostKind.None;

            foreach (var method in bucket.Methods)
            {
                if (method.Host == HostKind.None)
                {
                    diagnostics.Add(_rules.MissingHost(method.ComponentName, method.Span));
                    hosted = false;
                    continue;
                }
                if (host == HostKind.None)
                    host = method.Host;

                if (_rules.JudgeMethod(method) is { } diagnostic)
                    diagnostics.Add(diagnostic);
                else
                    methods.Add(method);
            }

            foreach (var reactive in bucket.Reactives)
            {
                if (reactive.Host == HostKind.None)
                {
                    diagnostics.Add(_rules.MissingHost(reactive.ComponentName, reactive.Span));
                    hosted = false;
                    continue;
                }
                if (host == HostKind.None)
                    host = reactive.Host;

                if (_rules.JudgeReactive(reactive) is { } diagnostic)
                    diagnostics.Add(diagnostic);
                else
                    reactives.Add(reactive);
            }

            // Hooks resolve against the surviving fields, even for components that end
            // up suppressed — an unresolvable hook deserves its diagnostic regardless.
            var wired = new Dictionary<string, SourceHook>(StringComparer.Ordinal);
            foreach (var hook in bucket.Hooks)
            {
                if (_rules.JudgeHook(hook) is { } diagnostic)
                {
                    diagnostics.Add(diagnostic);
                    continue;
                }

                if (_hooks.Resolve(hook, reactives) is not { } target)
                {
                    diagnostics.Add(_rules.UnmatchedHook(hook));
                    continue;
                }

                if (wired.ContainsKey(target.FieldName))
                    diagnostics.Add(_rules.DuplicateHook(hook, target.FieldName));
                else
                    wired.Add(target.FieldName, hook);
            }

            // Gates resolve against the surviving commands, even for components that
            // end up suppressed — an unresolvable gate deserves its diagnostic.
            var gates = new Dictionary<string, SourceCanExecute>(StringComparer.Ordinal);
            foreach (var gate in bucket.CanExecutes)
            {
                if (_rules.JudgeCanExecute(gate) is { } diagnostic)
                {
                    diagnostics.Add(diagnostic);
                    continue;
                }

                if (_canExecutes.Resolve(gate, methods) is not { } target)
                {
                    diagnostics.Add(_rules.UnmatchedCanExecute(gate));
                    continue;
                }

                if (gates.ContainsKey(target.MethodName))
                    diagnostics.Add(_rules.DuplicateCanExecute(gate, target.MethodName));
                else
                    gates.Add(target.MethodName, gate);
            }

            if (!hosted || (methods.Count == 0 && reactives.Count == 0))
                continue;

            components.Add(new CompiledComponent(
                Namespace: bucket.Namespace,
                Name: bucket.Name,
                Host: host,
                Methods: new EquatableArray<CompiledMethod>(
                    methods.Select(m => ToCompiled(m, gates, host)).ToArray()),
                Reactives: new EquatableArray<CompiledReactive>(
                    reactives.Select(r => ToCompiled(r, wired, host)).ToArray()),
                // Host policy: only XAML activation needs the parameterless bridge.
                // Razor injects through @inject; a store is constructed by the
                // container, whose own constructor injection already works.
                Injection: host is HostKind.Control && bucket.Injection is { } injection
                    ? new CompiledInjection(injection.ParameterTypes)
                    : null,
                Usings: _usings.Collect(ambient, methods, reactives)));
        }

        return Result<TempestCompilation>.Ok(new TempestCompilation(
            new EquatableArray<CompiledComponent>(components.ToArray()),
            new EquatableArray<TempestDiagnostic>(diagnostics.ToArray())));
    }

    private static CompiledMethod ToCompiled(
        SourceMethod method, IReadOnlyDictionary<string, SourceCanExecute> gates, HostKind host) => new(
        MethodName: method.MethodName,
        IsCommand: method.IsCommand,
        IsEvent: method.IsEvent,
        RunOnLoad: method.RunOnLoad,
        Kind: method.Kind,
        ResultType: method.ResultType,
        HasCancellationToken: method.HasCancellationToken,
        ParamType: method.ParamType,
        ParamTypeName: method.ParamTypeName,
        CanExecute: gates.TryGetValue(method.MethodName, out var gate)
            ? new CompiledCanExecute(gate.MemberName, gate.IsMethod)
            : null,
        Accessibility: StateAccessibility(host, method.Accessibility));

    private static CompiledReactive ToCompiled(
        SourceReactiveProperty reactive, IReadOnlyDictionary<string, SourceHook> wired, HostKind host) => new(
        FieldName: reactive.FieldName,
        PropertyName: reactive.PropertyName,
        TypeText: reactive.TypeText,
        Hook: wired.TryGetValue(reactive.FieldName, out var hook)
            ? new CompiledHook(hook.MethodName, hook.ReturnsTask)
            : null,
        Accessibility: StateAccessibility(host, reactive.Accessibility));

    /// <summary>Host policy on generated state accessibility. Blazor components mirror
    /// the member (markup compiles into the class, so private is reachable); XAML
    /// controls and headless stores get public states — WPF's binding engine reads only
    /// public properties, and a store's whole point is being consumed from outside.</summary>
    private static string StateAccessibility(HostKind host, string memberAccessibility)
        => host is HostKind.Control or HostKind.Store ? "public" : memberAccessibility;
}
