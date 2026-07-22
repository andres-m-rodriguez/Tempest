using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>A [Reactive] field exactly as a frontend read it — source data, before any
/// shape validation. Carries everything the shape rules judge; the compiler maps
/// survivors into its internal reactive model.</summary>
public sealed record SourceReactiveProperty(
    string Namespace,
    string ComponentName,
    string FieldName,
    /// <summary>The PascalCase twin the generated {Name}State property is named after.</summary>
    string PropertyName,
    string TypeText,
    /// <summary>The Tempest host base the component inherits; None is the TEM002 case.
    /// Razor parsing can only infer this (implicit Component, or @inherits).</summary>
    HostKind Host,
    /// <summary>False when the field breaks the [Reactive] shape rule (public, static,
    /// or its name equals its PascalCase twin).</summary>
    bool IsValidField,
    /// <summary>Of the field — the generated state property matches it.</summary>
    string Accessibility,
    /// <summary>True when the frontend read type names as written instead of resolving
    /// symbols, so the generated file must import <see cref="FileUsings"/> plus the
    /// project's ambient imports for them to resolve. A capability of the frontend, not
    /// its identity — the compiler never branches on where an entry came from.</summary>
    bool NeedsAmbientUsings,
    /// <summary>'\n'-joined using directives of the source file; empty when the frontend
    /// resolves symbols itself.</summary>
    string FileUsings,
    SourceSpan Span);
