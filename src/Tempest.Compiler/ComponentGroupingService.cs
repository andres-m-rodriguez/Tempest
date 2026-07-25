using Tempest.Parsing;

namespace Tempest.Compiler;

/// <summary>Deduplicates members by identity and buckets them per component. A combined
/// [Event, Command] method arrives in both of a document's method arrays with identical
/// flags — grouping by identity keeps one copy; the same rule collapses duplicates when
/// two frontends read one member.</summary>
internal sealed class ComponentGroupingService
{
    internal IReadOnlyList<ComponentBucket> Group(IReadOnlyList<TempestDocument> documents)
    {
        var methods = documents
            .SelectMany(d => d.Commands.Concat(d.Events))
            .GroupBy(m => (m.Namespace, m.ComponentName, m.MethodName, m.ParamType))
            .Select(g => g.First())
            .ToList();

        var reactives = documents
            .SelectMany(d => d.Reactives)
            .GroupBy(r => (r.Namespace, r.ComponentName, r.FieldName))
            .Select(g => g.First())
            .ToList();

        var hooks = documents
            .SelectMany(d => d.Hooks)
            .GroupBy(h => (h.Namespace, h.ComponentName, h.MethodName))
            .Select(g => g.First())
            .ToList();

        var canExecutes = documents
            .SelectMany(d => d.CanExecutes)
            .GroupBy(c => (c.Namespace, c.ComponentName, c.MemberName))
            .Select(g => g.First())
            .ToList();

        // At most one per component by construction; identity dedup collapses the
        // copies two frontends produce for one type.
        var injections = documents
            .SelectMany(d => d.Injections)
            .GroupBy(i => (i.Namespace, i.ComponentName))
            .Select(g => g.First())
            .ToList();

        var methodsByComponent = methods.ToLookup(m => (m.Namespace, m.ComponentName));
        var reactivesByComponent = reactives.ToLookup(r => (r.Namespace, r.ComponentName));
        var hooksByComponent = hooks.ToLookup(h => (h.Namespace, h.ComponentName));
        var canExecutesByComponent = canExecutes.ToLookup(c => (c.Namespace, c.ComponentName));
        var injectionByComponent = injections.ToDictionary(i => (i.Namespace, i.ComponentName));

        // Wiring-only components still get a bucket, so unresolvable hooks and gates
        // get diagnosed.
        return methods.Select(m => (m.Namespace, m.ComponentName))
            .Concat(reactives.Select(r => (r.Namespace, r.ComponentName)))
            .Concat(hooks.Select(h => (h.Namespace, h.ComponentName)))
            .Concat(canExecutes.Select(c => (c.Namespace, c.ComponentName)))
            .Distinct()
            .Select(key => new ComponentBucket(
                key.Namespace,
                key.ComponentName,
                [.. methodsByComponent[key]],
                [.. reactivesByComponent[key]],
                [.. hooksByComponent[key]],
                [.. canExecutesByComponent[key]],
                injectionByComponent.TryGetValue(key, out var injection) ? injection : null))
            .ToList();
    }
}
