using Tempest.Parsing;

namespace Tempest.Compiler;

/// <summary>Which [Reactive] field an [OnChanged] hook watches: an explicit target
/// naming the field or its PascalCase twin, else the On{Field}Changed name convention.
/// Resolution runs against valid fields only — a hook on an invalid field is unmatched
/// (the field's own diagnostic already fired). Hook and field may come from different
/// sources of one component.</summary>
internal sealed class HookResolutionService
{
    internal SourceReactiveProperty? Resolve(SourceHook hook, IReadOnlyList<SourceReactiveProperty> reactives)
    {
        if (hook.ExplicitTarget is { } target)
            return reactives.FirstOrDefault(r => r.FieldName == target || r.PropertyName == target);

        const string Head = "On";
        const string Tail = "Changed";
        var name = hook.MethodName;
        if (name.Length <= Head.Length + Tail.Length ||
            !name.StartsWith(Head, StringComparison.Ordinal) ||
            !name.EndsWith(Tail, StringComparison.Ordinal))
            return null;

        var twin = name.Substring(Head.Length, name.Length - Head.Length - Tail.Length);
        return reactives.FirstOrDefault(r => r.PropertyName == twin);
    }
}
