namespace Tempest.Model;

/// <summary>The wired half of a reactive change hook: the [OnChanged] method the
/// emitter invokes when the value changes. An ordinary user method — no partial
/// counterpart is generated.</summary>
public sealed record HookModel(
    string MethodName,
    bool ReturnsTask);

/// <summary>A validated [Reactive] field, holding only what emission needs. Its
/// entry-point counterpart (with shape flags and spans) is
/// <see cref="Entry.EntryReactive"/>; the compiler maps one into the other once the
/// shape rules pass. The field's type is carried as source text, emitted back verbatim.</summary>
public sealed record ReactiveModel(
    string FieldName,
    /// <summary>The PascalCase twin the generated {Name}State property is named after.</summary>
    string PropertyName,
    string TypeText,
    HookModel? Hook,
    /// <summary>Of the field — the generated state property matches it.</summary>
    string Accessibility);
