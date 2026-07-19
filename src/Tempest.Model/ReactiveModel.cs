namespace Tempest.Model;

/// <summary>The user-implemented half of a reactive change hook
/// (<c>partial … On{Name}Changed(T value)</c>), when a valid one exists.</summary>
public sealed record HookModel(
    /// <summary>As written, e.g. "private" — may be empty.</summary>
    string Accessibility,
    bool ReturnsTask,
    string ParamName,
    string MethodName);

/// <summary>A validated [Reactive] field, holding only what emission needs. Its
/// entry-point counterpart (with shape flags and spans) is
/// <see cref="Entry.EntryReactive"/>; the assembler maps one into the other once the
/// shape rules pass. The field's type is carried as source text, emitted back verbatim.</summary>
public sealed record ReactiveModel(
    string FieldName,
    /// <summary>The PascalCase twin the generated {Name}State property is named after.</summary>
    string PropertyName,
    string TypeText,
    HookModel? Hook,
    /// <summary>Of the field — the generated state property matches it.</summary>
    string Accessibility);
