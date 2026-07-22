using Tempest.Compiler;
using Tempest.Pipeline;

namespace Tempest.Emit;

/// <summary>Emit input breaking its contract — a shell bug, not a user-code problem:
/// a null component. Detected at the boundary before any emission starts.</summary>
public sealed record InvalidEmitInputError(string Message) : IError
{
    public string Code => "EMT001";

    internal static InvalidEmitInputError? Check(CompiledComponent? component)
        => component is null ? new("The component must not be null.") : null;
}
