namespace Tempest.RazorParser;

/// <summary>A line directive as written: "@using System.Text" is Name "using", Value
/// "System.Text". The reader decides which names matter; unknown directives ride along
/// harmlessly.</summary>
internal sealed record DirectiveNode(string Name, string Value, int Start, int End) : Node(Start, End);
