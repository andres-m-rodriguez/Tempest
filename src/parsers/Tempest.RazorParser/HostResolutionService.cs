using Tempest.Parsing;

namespace Tempest.RazorParser;

/// <summary>Which Tempest host base an @inherits names, matched by simple name (text
/// parsing cannot resolve symbols). Absence is the implicit Component case of .razor
/// files; any other base is None, the TEM002 case, kept so the compiler can diagnose
/// instead of the parser dropping entries.</summary>
internal sealed class HostResolutionService
{
    internal HostKind Resolve(string? baseType)
    {
        if (baseType is not { Length: > 0 })
            return HostKind.Component;

        var name = baseType.Trim();
        var dot = name.LastIndexOf('.');
        var simpleName = dot >= 0 ? name.Substring(dot + 1) : name;

        return simpleName switch
        {
            "StatefulComponent" => HostKind.Component,
            "StatefulLayoutComponent" => HostKind.LayoutComponent,
            "StatefulControl" => HostKind.Control,
            "StatefulPage" => HostKind.Control,
            "StatefulStore" => HostKind.Store,
            _ => HostKind.None,
        };
    }
}
