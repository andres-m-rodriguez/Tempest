using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>Two sources registered under one component name — a shell bug, not a
/// user-code problem: component names are the pipeline's identity and must be unique.</summary>
public sealed record DuplicateComponentError(string ComponentName) : IError
{
    public string Code => "PRS001";

    public string Message => $"A component named '{ComponentName}' is already registered.";
}
