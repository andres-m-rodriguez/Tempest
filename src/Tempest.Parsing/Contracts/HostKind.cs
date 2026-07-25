namespace Tempest.Parsing;

/// <summary>Which Tempest host base the component inherits — domain data about the
/// user's class, not frontend identity. A closed set: the runtime defines the bases and
/// TEM002 requires one, so every frontend must recognize them anyway. Host-specific
/// policy (e.g. store state properties defaulting to public) resolves in the compiler
/// from this kind; the emitter stays single and neutral.</summary>
public enum HostKind
{
    /// <summary>No recognized host base — the TEM002 case, kept so the compiler can
    /// diagnose instead of the parser dropping the entry.</summary>
    None,
    /// <summary>StatefulComponent — Blazor; implicit for .razor files.</summary>
    Component,
    /// <summary>StatefulLayoutComponent — Blazor layouts.</summary>
    LayoutComponent,
    /// <summary>StatefulControl or StatefulPage — XAML.</summary>
    Control,
    /// <summary>StatefulStore — headless, shared by any UI.</summary>
    Store,
}
