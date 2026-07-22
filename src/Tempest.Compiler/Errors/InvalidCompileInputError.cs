using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.Compiler;

/// <summary>Compile input breaking its contract — a shell bug, not a user-code problem:
/// a null document sequence, or a null document inside it. Detected at the boundary
/// before any compiling starts.</summary>
public sealed record InvalidCompileInputError(string Message) : IError
{
    public string Code => "CMP001";

    internal static InvalidCompileInputError? Check(
        IEnumerable<TempestDocument>? documents, out List<TempestDocument> validated)
    {
        validated = [];

        if (documents is null)
            return new("The document sequence must not be null.");

        foreach (var document in documents)
        {
            if (document is null)
                return new("The document sequence must not contain null documents.");
            validated.Add(document);
        }

        return null;
    }
}
