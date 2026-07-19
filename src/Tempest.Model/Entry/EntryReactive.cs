namespace Tempest.Model.Entry;

/// <summary>An [OnChanged] hook method exactly as a frontend read it — entry-point data,
/// before resolution. Which [Reactive] field it watches is the compiler stage's decision:
/// the explicit attribute argument when present, else the On{Field}Changed name
/// convention. Unresolvable or malformed hooks become diagnostics there.</summary>
public sealed record EntryHook(
    string Namespace,
    string ComponentName,
    string MethodName,
    /// <summary>The attribute's argument when written as [OnChanged("...")] or
    /// [OnChanged(nameof(...))]: a field name or its PascalCase twin. Null for bare
    /// [OnChanged], which resolves by the On{Field}Changed name convention.</summary>
    string? ExplicitTarget,
    bool ReturnsTask,
    /// <summary>Total parameter count — the shape rule demands exactly one (the new value).</summary>
    int ParameterCount,
    SourceSpan Span);

/// <summary>A [Reactive] field exactly as a frontend read it — entry-point data, before
/// any shape validation. Carries everything the shape rules judge; the compiler maps
/// survivors into the internal <see cref="ReactiveModel"/>, wiring any [OnChanged] hook
/// during resolution.</summary>
public sealed record EntryReactive(
    string Namespace,
    string ComponentName,
    string FieldName,
    /// <summary>The PascalCase twin the generated {Name}State property is named after.</summary>
    string PropertyName,
    string TypeText,
    /// <summary>True when the component inherits a Tempest host base (Blazor's
    /// StatefulComponent/StatefulLayoutComponent). Razor parsing can only infer this.</summary>
    bool InheritsHostBase,
    /// <summary>False when the field breaks the [Reactive] shape rule (public, static,
    /// or its name equals its PascalCase twin).</summary>
    bool IsValidField,
    /// <summary>Of the field — the generated state property matches it.</summary>
    string Accessibility,
    bool FromRazor,
    /// <summary>'\n'-joined @using directives of the source .razor file; empty for C# sources.</summary>
    string FileUsings,
    SourceSpan Span);
