using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.Compiler;

/// <summary>The SPEC's shape rules behind one seam: judges a member and returns the
/// diagnostic it earns, or null for a survivor. TEM002 (no host base) is component
/// level and judged by the caller through <see cref="MissingHost"/> — a member failing
/// it invalidates its whole component, not just itself.</summary>
internal sealed class ShapeRuleService
{
    internal TempestDiagnostic MissingHost(string component, SourceSpan span) => new(
        Id: "TEM002",
        Title: "Component must inherit a Tempest host base",
        Message: $"'{component}' uses [Command], [Event] or [Reactive] but inherits no Tempest host base (StatefulComponent, StatefulLayoutComponent, StatefulControl, or StatefulStore)",
        ComponentName: component,
        Severity: TempestDiagnosticSeverity.Error,
        Span: span);

    internal TempestDiagnostic? JudgeMethod(SourceMethod method)
    {
        if (method.IsEvent &&
            (method.ParameterCount != 1 ||
             !method.ParamIsNestedRecordOfComponent ||
             (method.HasCancellationToken && !method.IsCommand)))
            return new(
                Id: "TEM001",
                Title: "[Event] handler has the wrong shape",
                Message: $"[Event] handler '{method.MethodName}' must take exactly one parameter: a nested record of this component (an [Event, Command] may add a trailing CancellationToken)",
                ComponentName: method.ComponentName,
                Severity: TempestDiagnosticSeverity.Error,
                Span: method.Span);

        if (method.IsCommand && !method.IsEvent && method.ParameterCount != 0)
            return new(
                Id: "TEM003",
                Title: "[Command] method has the wrong shape",
                Message: $"[Command] method '{method.MethodName}' must be parameterless or take only a trailing CancellationToken, unless it is also an [Event] handler",
                ComponentName: method.ComponentName,
                Severity: TempestDiagnosticSeverity.Error,
                Span: method.Span);

        return null;
    }

    internal TempestDiagnostic? JudgeReactive(SourceReactiveProperty reactive)
        => reactive.IsValidField
            ? null
            : new(
                Id: "TEM007",
                Title: "[Reactive] field has the wrong shape",
                Message: $"[Reactive] field '{reactive.FieldName}' must be a non-public, non-static field whose name differs from its PascalCase twin",
                ComponentName: reactive.ComponentName,
                Severity: TempestDiagnosticSeverity.Error,
                Span: reactive.Span);

    internal TempestDiagnostic? JudgeHook(SourceHook hook)
        => hook.ParameterCount == 1
            ? null
            : new(
                Id: "TEM009",
                Title: "[OnChanged] hook has the wrong shape",
                Message: $"[OnChanged] hook '{hook.MethodName}' must take exactly one parameter: the field's new value",
                ComponentName: hook.ComponentName,
                Severity: TempestDiagnosticSeverity.Error,
                Span: hook.Span);

    internal TempestDiagnostic UnmatchedHook(SourceHook hook) => new(
        Id: "TEM008",
        Title: "[OnChanged] hook matches no [Reactive] field",
        Message: $"[OnChanged] hook '{hook.MethodName}' matches no [Reactive] field of this component; name it On{{Field}}Changed or pass the field name to the attribute",
        ComponentName: hook.ComponentName,
        Severity: TempestDiagnosticSeverity.Error,
        Span: hook.Span);

    internal TempestDiagnostic DuplicateHook(SourceHook hook, string fieldName) => new(
        Id: "TEM010",
        Title: "[Reactive] field has multiple change hooks",
        Message: $"[Reactive] field '{fieldName}' already has a change hook; '{hook.MethodName}' is ignored",
        ComponentName: hook.ComponentName,
        Severity: TempestDiagnosticSeverity.Warning,
        Span: hook.Span);
}
