namespace Tempest.RazorParser;

/// <summary>Everything the razor frontend needs about one razor document, as plain
/// values — nothing about files. The shell that owns the file system derives these
/// (component name from the file name, fallback namespace from RootNamespace plus the
/// project-relative directory) and keeps the association back to real paths; tests just
/// new one up.</summary>
public sealed record RazorSource(
    /// <summary>
    /// The component's class name. Sanitized to a valid identifier by the
    /// parser, so a shell may pass a raw file name stem.
    /// </summary>
    string ComponentName,
    string Text,
    /// <summary>
    /// The namespace to use when the document has no @namespace directive,
    /// verbatim; empty when unknown.
    /// </summary>
    string FallbackNamespace = ""
    );
