namespace Tempest.Emit;

/// <summary>One emitted source file: the stable hint name the generator registers it
/// under ("{Namespace}.{Name}.Tempest.g.cs") and the complete C# text. Plain values, so
/// the incremental pipeline caches emission by equality like every other stage.</summary>
public sealed record GeneratedFile(
    string HintName,
    string Source);
