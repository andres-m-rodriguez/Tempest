namespace Tempest.Compiler;

/// <summary>The wired half of a reactive change hook: the [OnChanged] method the
/// emitter invokes when the value changes. An ordinary user method — no partial
/// counterpart is generated.</summary>
public sealed record CompiledHook(
    string MethodName,
    bool ReturnsTask);
