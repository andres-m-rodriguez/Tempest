using Tempest.Pipeline;

namespace Tempest.RazorParser;

/// <summary>A <see cref="RazorSource"/> that breaks its own contract — a shell bug, not
/// a user-code problem: a null source, null text, or a missing component name. Detected
/// at the client boundary before any parsing starts.</summary>
public sealed record InvalidRazorSourceError(string ComponentName, string Message) : IError
{
    public string Code => "RZP001";

    internal static InvalidRazorSourceError? Check(RazorSource? source) => source switch
    {
        null => new("", "RazorSource must not be null."),
        { ComponentName: not { Length: > 0 } } => new(
            source.ComponentName ?? "",
            "RazorSource.ComponentName must be non-empty — the shell derives it (e.g. from the file name stem)."),
        { Text: null } => new(
            source.ComponentName,
            "RazorSource.Text must not be null; an empty document is an empty string."),
        { FallbackNamespace: null } => new(
            source.ComponentName,
            "RazorSource.FallbackNamespace must be an empty string rather than null when unknown."),
        _ => null,
    };
}
