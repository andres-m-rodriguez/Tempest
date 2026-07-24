namespace Tempest.Compiler;

/// <summary>The wired enablement gate of a command: the [CanExecute] member the
/// generated state reads as its ICommand.CanExecute predicate. An ordinary user
/// member — no partial counterpart is generated.</summary>
public sealed record CompiledCanExecute(
    string MemberName,
    /// <summary>True for a method — the emitted predicate calls it; a property is read.</summary>
    bool IsMethod);
