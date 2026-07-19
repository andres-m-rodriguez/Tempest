namespace Tempest.Model.Entry;

/// <summary>A reactive change-hook candidate exactly as a frontend read it — kept even
/// when not partial, so the assembler can warn (TEM008) before dropping it.</summary>
public sealed record EntryHook(
    /// <summary>As written, e.g. "private" — may be empty.</summary>
    string Accessibility,
    bool ReturnsTask,
    string ParamName,
    bool IsPartial,
    string MethodName,
    SourceSpan Span);

/// <summary>A [Reactive] field exactly as a frontend read it — entry-point data, before
/// any shape validation. Carries everything the shape rules judge; the assembler maps
/// survivors into the internal <see cref="ReactiveModel"/>.</summary>
public sealed record EntryReactive(
    string Namespace,
    string ComponentName,
    string FieldName,
    /// <summary>The PascalCase twin the generated {Name}State property is named after.</summary>
    string PropertyName,
    string TypeText,
    EntryHook? Hook,
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
