using Tempest.Model.Entry;

namespace Tempest.Parsing;

/// <summary>The contract every parse frontend implements: one source in, the entries it
/// declares out. TSource is the frontend's own plain-value description of one input
/// (a .razor file's text, a C# type symbol, …), so each frontend stays testable without
/// a compiler harness.</summary>
public interface IComponentParser<in TSource>
{
    SourceEntries Parse(TSource source);
}
