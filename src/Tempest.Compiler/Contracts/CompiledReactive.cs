namespace Tempest.Compiler;

/// <summary>A validated [Reactive] field, holding only what emission needs. Its source
/// counterpart (with shape flags and spans) is
/// <see cref="Tempest.Parsing.SourceReactiveProperty"/>; the compiler maps one into the
/// other once the shape rules pass. The field's type is carried as source text, emitted
/// back verbatim.</summary>
public sealed record CompiledReactive(
    string FieldName,
    /// <summary>The PascalCase twin the generated {Name}State property is named after.</summary>
    string PropertyName,
    string TypeText,
    CompiledHook? Hook,
    /// <summary>Of the field — the generated state property matches it.</summary>
    string Accessibility);
