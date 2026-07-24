using Tempest.Parsing;

namespace Tempest.Compiler;

/// <summary>Which [Command] a [CanExecute] member gates: an explicit target naming the
/// command's method, else the On{Command}CanExecute name convention — the same grammar
/// as [OnChanged]'s On{Field}Changed. Resolution runs against valid commands only — a
/// gate on an invalid command is unmatched (the command's own diagnostic already
/// fired). Gate and command may come from different sources of one component.</summary>
internal sealed class CanExecuteResolutionService
{
    /// <summary>The gated command's method, or null when nothing matches.</summary>
    internal SourceMethod? Resolve(SourceCanExecute gate, IReadOnlyList<SourceMethod> commands)
    {
        if (gate.ExplicitTarget is { } target)
            return commands.FirstOrDefault(m => m.IsCommand && m.MethodName == target);

        const string Head = "On";
        const string Tail = "CanExecute";
        var name = gate.MemberName;
        if (name.Length <= Head.Length + Tail.Length ||
            !name.StartsWith(Head, StringComparison.Ordinal) ||
            !name.EndsWith(Tail, StringComparison.Ordinal))
            return null;

        var command = name.Substring(Head.Length, name.Length - Head.Length - Tail.Length);
        return commands.FirstOrDefault(m => m.IsCommand && m.MethodName == command);
    }
}
