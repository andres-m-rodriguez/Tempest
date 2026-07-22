using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Tempest.Pipeline;
using Tempest.Parsing;

namespace Tempest.RazorParser;

/// <summary>Roslyn behind one seam: parses a @code block's C# as members of a fake
/// class, and reads the textual facts entries carry — attribute names, accessibility,
/// return classification, hook targets, spans mapped back onto the original file.</summary>
internal sealed class CSharpSyntaxService
{
    // Each @code block is parsed as the body of this fake class so member syntax applies.
    private const string Prefix = "class __Razor{";

    internal ClassDeclarationSyntax? ParseFakeClass(string blockText)
        => SyntaxFactory.ParseCompilationUnit(Prefix + blockText + "}")
            .Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();

    /// <summary>Maps a span inside the fake-class parse back onto the original document.
    /// Position-only by contract — file identity stays with the shell.</summary>
    internal SourceSpan MapSpan(SourceText sourceText, TextSpan spanInEntry, int blockStart)
    {
        var start = spanInEntry.Start - Prefix.Length + blockStart;
        if (start < 0 || start + spanInEntry.Length > sourceText.Length)
            return SourceSpan.None;

        var fileSpan = new TextSpan(start, spanInEntry.Length);
        var lines = sourceText.Lines.GetLinePositionSpan(fileSpan);
        return new SourceSpan(
            start, spanInEntry.Length,
            lines.Start.Line, lines.Start.Character, lines.End.Line, lines.End.Character);
    }

    /// <summary>Textual return classification: matches on the simple type name so both
    /// `Task&lt;T&gt;` and `System.Threading.Tasks.Task&lt;T&gt;` classify alike.</summary>
    internal (ReturnKind Kind, string? ResultType) ClassifyReturnText(string rt)
    {
        rt = rt.Trim();
        if (rt == "void")
            return (ReturnKind.Void, null);

        var open = rt.IndexOf('<');
        var head = open < 0 ? rt : rt.Substring(0, open);
        var lastDot = head.LastIndexOf('.');
        var simpleName = lastDot >= 0 ? head.Substring(lastDot + 1) : head;
        var inner = open < 0 ? null : rt.Substring(open + 1, rt.Length - open - 2);

        return simpleName switch
        {
            "Task" => open < 0 ? (ReturnKind.Task, null) : (ReturnKind.TaskOfT, inner),
            "ValueTask" => open < 0 ? (ReturnKind.ValueTask, null) : (ReturnKind.ValueTaskOfT, inner),
            _ => (ReturnKind.Sync, rt),
        };
    }

    /// <summary>The [OnChanged] argument as a field/property name: a string literal's
    /// value, or the identifier inside nameof(...) (last segment when qualified). Null
    /// when the attribute is bare — the name convention resolves the target instead.</summary>
    internal string? ExplicitHookTarget(AttributeSyntax attribute)
    {
        var expression = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        switch (expression)
        {
            case LiteralExpressionSyntax { Token.Value: string literal }:
                return literal;
            case InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } inv
                when inv.ArgumentList.Arguments.Count == 1:
            {
                var target = inv.ArgumentList.Arguments[0].Expression.ToString();
                var dot = target.LastIndexOf('.');
                return dot >= 0 ? target.Substring(dot + 1) : target;
            }
            default:
                return null;
        }
    }

    /// <summary>Accessibility modifiers as written. Methods and fields default to private
    /// (a class member's default); hook accessibility is contractually as-written, so its
    /// default is empty.</summary>
    internal string AccessibilityModifierText(SyntaxTokenList modifiers, string defaultTo)
    {
        var text = string.Join(" ", modifiers
            .Where(m => m.IsKind(SyntaxKind.PrivateKeyword) ||
                        m.IsKind(SyntaxKind.ProtectedKeyword) ||
                        m.IsKind(SyntaxKind.InternalKeyword) ||
                        m.IsKind(SyntaxKind.PublicKeyword))
            .Select(m => m.Text));
        return text.Length > 0 ? text : defaultTo;
    }

    internal bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string shortName)
        => lists.SelectMany(l => l.Attributes).Any(a => SimpleAttributeName(a.Name.ToString()) == shortName);

    /// <summary>Qualified attribute names classify by simple name, with or without the
    /// conventional Attribute suffix.</summary>
    internal string SimpleAttributeName(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name.Substring(dot + 1);
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - "Attribute".Length);
        return name;
    }
}
