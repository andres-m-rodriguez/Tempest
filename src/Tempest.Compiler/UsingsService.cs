using Tempest.Pipeline;
using Tempest.Parsing;

namespace Tempest.Compiler;

/// <summary>The using directives a component's generated file needs. Nothing when every
/// member resolved symbols itself; otherwise the members' file usings plus the
/// project's ambient imports (every _Imports.razor), sorted, deduplicated, and stripped
/// of the usings every generated file carries anyway.</summary>
internal sealed class UsingsService
{
    private static readonly HashSet<string> StandardUsings =
        ["System", "System.Collections.Generic", "System.Threading", "System.Threading.Tasks"];

    internal EquatableArray<string> Collect(
        IReadOnlyList<string> ambientUsings,
        IReadOnlyList<SourceMethod> methods,
        IReadOnlyList<SourceReactiveProperty> reactives)
    {
        if (!methods.Any(m => m.NeedsAmbientUsings) && !reactives.Any(r => r.NeedsAmbientUsings))
            return EquatableArray<string>.Empty;

        var usings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var joined in ambientUsings)
            Add(usings, joined);
        foreach (var method in methods)
            Add(usings, method.FileUsings);
        foreach (var reactive in reactives)
            Add(usings, reactive.FileUsings);

        return new EquatableArray<string>([.. usings]);
    }

    private static void Add(SortedSet<string> target, string joined)
    {
        foreach (var directive in joined.Split('\n'))
        {
            if (directive.Length > 0 && !StandardUsings.Contains(directive))
                target.Add(directive);
        }
    }
}
