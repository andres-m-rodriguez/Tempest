namespace Tempest.Parsing;

/// <summary>How a [Command] method returns its work — decides which state class the
/// emitter news up and how the call is adapted into the async lifecycle.</summary>
public enum ReturnKind
{
    Void,
    Task,
    TaskOfT,
    ValueTask,
    ValueTaskOfT,
    Sync,
}
