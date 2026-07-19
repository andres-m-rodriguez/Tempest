using System.Collections.Immutable;
using System.Text;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Tempest.Model;
using Tempest.Model.Entry;
using Tempest.Parsing;
using RazorSpan = Microsoft.AspNetCore.Razor.Language.SourceSpan;
using SourceSpan = Tempest.Model.SourceSpan;

namespace Tempest.RazorParser;

/// <summary>The .razor frontend of the pipeline: runs the official Razor compiler
/// (Microsoft.AspNetCore.Razor.Language) over one file to locate its @code blocks and
/// directives, then parses each block's C# with Roslyn into model records. Base types
/// cannot be resolved from text, so any @inherits is trusted (its absence is flagged via
/// InheritsHostBase); type names are carried as written.</summary>
public sealed class RazorParser : IComponentParser<RazorSource>
{
    // Each @code block is parsed as the body of this fake class so member syntax applies.
    private const string Prefix = "class __Razor{";

    // The Razor IR consumes directive nodes (RazorSyntaxTree's node API is internal, and
    // @namespace/@inherits leave no DirectiveIntermediateNode behind), so both are read
    // back off what the engine computed from them:
    //  - @namespace flows into NamespaceDeclarationIntermediateNode.Content; a sentinel
    //    root namespace baked into the engine tells "directive present" apart from "the
    //    engine fell back to its own RootNamespace + path computation". The fallback
    //    namespace itself is computed locally (ResolveNamespace) because the engine cannot
    //    represent an empty root namespace / target path (it would emit
    //    "__GeneratedComponent" instead of "").
    //  - @inherits flows into ClassDeclarationIntermediateNode.BaseType; absence leaves
    //    the component default base type below.
    private const string SentinelNamespace = "__TempestNamespaceSentinel";
    private const string DefaultComponentBase = "Microsoft.AspNetCore.Components.ComponentBase";

    // The engine computes its namespace from RelativePath, which must be non-null and no
    // longer than FilePath for the @namespace directive to be honored at all; parsing
    // works purely from source text, so both are pinned to a sentinel file name and the
    // real path is carried only in the spans this parser maps itself.
    private static readonly RazorSourceDocumentProperties DocumentProperties =
        new(filePath: "/_.razor", relativePath: "_.razor");

    private static readonly RazorProjectEngine Engine = RazorProjectEngine.Create(
        RazorConfiguration.Default,
        RazorProjectFileSystem.Create("/"),
        builder => builder.SetRootNamespace(SentinelNamespace));

    public SourceEntries Parse(RazorSource source)
    {
        var text = source.Text;
        if (!text.Contains("[Command") && !text.Contains("[Event") &&
            !text.Contains("[Reactive") && !text.Contains("[OnChanged"))
            return SourceEntries.Empty;

        var componentName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(source.FilePath));

        var document = RazorSourceDocument.Create(text, DocumentProperties);
        var codeDocument = Engine.Process(
            document, FileKinds.Component, Array.Empty<RazorSourceDocument>(), Array.Empty<TagHelperDescriptor>());
        var ir = codeDocument.GetDocumentIntermediateNode();

        var nsNode = Descendants(ir).OfType<NamespaceDeclarationIntermediateNode>().FirstOrDefault();
        var ns = nsNode is { Content: { Length: > 0 } content } && content != SentinelNamespace
            ? content
            : ResolveNamespace(source.RootNamespace, source.TargetPath);

        var classNode = Descendants(ir).OfType<ClassDeclarationIntermediateNode>().FirstOrDefault();

        // We cannot resolve base types from text; trust any @inherits, flag its absence.
        var inherits = classNode?.BaseType is { Length: > 0 } baseType &&
                       baseType != DefaultComponentBase &&
                       baseType != "global::" + DefaultComponentBase;

        // Type names are emitted as written, so the generated file needs this file's usings.
        // Only directives with a source span are the file's own (none are synthesized in
        // this engine configuration, but that is not contractual); the engine already
        // strips the optional trailing semicolon.
        var fileUsings = string.Join("\n", Descendants(ir)
            .OfType<UsingDirectiveIntermediateNode>()
            .Where(u => u.Source is not null)
            .Select(u => u.Content.Trim().TrimEnd(';')));

        var sourceText = SourceText.From(text);
        var methods = new List<EntryMethod>();
        var reactiveFields = new List<(string FieldName, string TypeText, bool Valid, string Accessibility, SourceSpan Span)>();
        var hooks = new List<EntryHook>();

        foreach (var (blockStart, blockText) in ExtractCodeBlocks(text, classNode))
        {
            var unit = SyntaxFactory.ParseCompilationUnit(Prefix + blockText + "}");
            if (unit.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault() is not { } cls)
                continue;

            var recordNames = cls.Members
                .OfType<RecordDeclarationSyntax>()
                .Select(r => r.Identifier.ValueText)
                .ToImmutableHashSet();

            foreach (var fld in cls.Members.OfType<FieldDeclarationSyntax>())
            {
                if (!HasAttribute(fld.AttributeLists, "Reactive"))
                    continue;

                var badModifiers =
                    fld.Modifiers.Any(SyntaxKind.StaticKeyword) ||
                    fld.Modifiers.Any(SyntaxKind.PublicKeyword);
                var fieldAccessibility = AccessibilityModifierText(fld.Modifiers);

                foreach (var variable in fld.Declaration.Variables)
                    reactiveFields.Add((
                        variable.Identifier.ValueText,
                        fld.Declaration.Type.ToString(),
                        !badModifiers,
                        fieldAccessibility,
                        MapSpan(source.FilePath, sourceText, variable.Identifier.Span, blockStart)));
            }

            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                var span = MapSpan(source.FilePath, sourceText, method.Identifier.Span, blockStart);
                var methodName = method.Identifier.ValueText;

                var isCommand = false;
                var isEvent = false;
                AttributeSyntax? onChanged = null;
                foreach (var attr in method.AttributeLists.SelectMany(l => l.Attributes))
                {
                    var name = SimpleAttributeName(attr.Name.ToString());
                    if (name == "Command") isCommand = true;
                    if (name == "Event") isEvent = true;
                    if (name == "OnChanged") onChanged ??= attr;
                }

                // Hooks are declared by attribute; which field they watch is resolved by
                // the compiler stage, so only the raw facts are recorded here.
                if (onChanged is not null)
                {
                    hooks.Add(new EntryHook(
                        Namespace: ns,
                        ComponentName: componentName,
                        MethodName: methodName,
                        ExplicitTarget: ExplicitHookTarget(onChanged),
                        ReturnsTask: method.ReturnType.ToString() != "void",
                        ParameterCount: method.ParameterList.Parameters.Count,
                        Span: span));
                }

                if (!isCommand && !isEvent)
                    continue;

                var (kind, resultType) = ClassifyReturnText(method.ReturnType.ToString());

                var parameters = method.ParameterList.Parameters;
                var hasCt = parameters.Count > 0 && parameters[parameters.Count - 1].Type is { } lastType &&
                            (lastType.ToString() == "CancellationToken" ||
                             lastType.ToString().EndsWith(".CancellationToken", StringComparison.Ordinal));
                var effective = hasCt ? parameters.Count - 1 : parameters.Count;

                string? paramType = null;
                string? paramTypeName = null;
                var paramIsNestedRecord = false;
                if (effective == 1 && parameters[0].Type is { } pt)
                {
                    paramType = pt.ToString();
                    var lastDot = paramType.LastIndexOf('.');
                    paramTypeName = lastDot >= 0 ? paramType.Substring(lastDot + 1) : paramType;
                    paramIsNestedRecord = recordNames.Contains(paramTypeName);
                }

                methods.Add(new EntryMethod(
                    Namespace: ns,
                    ComponentName: componentName,
                    MethodName: methodName,
                    IsCommand: isCommand,
                    IsEvent: isEvent,
                    Kind: kind,
                    ResultType: resultType,
                    HasCancellationToken: hasCt,
                    ParameterCount: effective,
                    ParamType: paramType,
                    ParamTypeName: paramTypeName,
                    ParamIsNestedRecordOfComponent: paramIsNestedRecord,
                    InheritsHostBase: inherits,
                    Accessibility: AccessibilityModifierText(method.Modifiers),
                    FromRazor: true,
                    FileUsings: fileUsings,
                    Span: span));
            }
        }

        var reactives = new List<EntryReactive>();
        foreach (var (fieldName, typeText, validModifiers, accessibility, span) in reactiveFields)
        {
            var propertyName = ToPascal(fieldName);
            reactives.Add(new EntryReactive(
                Namespace: ns,
                ComponentName: componentName,
                FieldName: fieldName,
                PropertyName: propertyName,
                TypeText: typeText,
                InheritsHostBase: inherits,
                IsValidField: validModifiers && propertyName != fieldName && propertyName.Length > 0,
                Accessibility: accessibility,
                FromRazor: true,
                FileUsings: fileUsings,
                Span: span));
        }

        return new SourceEntries(
            new EquatableArray<EntryMethod>(methods.ToArray()),
            new EquatableArray<EntryReactive>(reactives.ToArray()),
            new EquatableArray<EntryHook>(hooks.ToArray()));
    }

    /// <summary>The [OnChanged] argument as a field/property name: a string literal's
    /// value, or the identifier inside nameof(...) (last segment when qualified). Null
    /// when the attribute is bare — the name convention resolves the target instead.</summary>
    private static string? ExplicitHookTarget(AttributeSyntax attribute)
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

    /// <summary>Each @code block's contents as (offset in file, text). The Razor engine
    /// hoists every @code block to a class-level CSharpCode node whose source span points
    /// verbatim into the original file (roundtrip-verified by tests), which is what makes
    /// hand-rolled brace matching unnecessary: the engine's own C# tokenizer has already
    /// skipped strings, verbatim strings, chars, and comments.</summary>
    private static IEnumerable<(int Start, string Text)> ExtractCodeBlocks(
        string text, ClassDeclarationIntermediateNode? classNode)
    {
        if (classNode is null)
            yield break;

        foreach (var codeNode in classNode.Children.OfType<CSharpCodeIntermediateNode>())
        {
            var span = codeNode.Source
                ?? codeNode.Children.OfType<IntermediateToken>().FirstOrDefault(t => t.IsCSharp)?.Source;
            if (span is not RazorSpan s)
                continue;
            if (s.AbsoluteIndex < 0 || s.Length < 0 || s.AbsoluteIndex + s.Length > text.Length)
                continue;

            // Slice the original text rather than trusting token content, so span math
            // stays correct by construction even if the engine ever normalizes content.
            yield return (s.AbsoluteIndex, text.Substring(s.AbsoluteIndex, s.Length));
        }
    }

    /// <summary>Textual return classification: matches on the simple type name so both
    /// `Task&lt;T&gt;` and `System.Threading.Tasks.Task&lt;T&gt;` classify alike.</summary>
    internal static (ReturnKind Kind, string? ResultType) ClassifyReturnText(string rt)
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

    /// <summary>The fallback when no @namespace directive is present: RootNamespace + the
    /// TargetPath's directory segments (each sanitized), '.'-joined.</summary>
    internal static string ResolveNamespace(string rootNamespace, string targetPath)
    {
        var segments = new List<string>();
        if (!string.IsNullOrEmpty(rootNamespace))
            segments.Add(rootNamespace);

        var dir = targetPath.Length == 0 ? null : Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
            segments.AddRange(dir
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizeIdentifier));

        return string.Join(".", segments);
    }

    /// <summary>Accessibility modifiers as written, defaulting to private (a class member's default).</summary>
    private static string AccessibilityModifierText(SyntaxTokenList modifiers)
    {
        var text = string.Join(" ", modifiers
            .Where(m => m.IsKind(SyntaxKind.PrivateKeyword) ||
                        m.IsKind(SyntaxKind.ProtectedKeyword) ||
                        m.IsKind(SyntaxKind.InternalKeyword) ||
                        m.IsKind(SyntaxKind.PublicKeyword))
            .Select(m => m.Text));
        return text.Length > 0 ? text : "private";
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string shortName)
        => lists.SelectMany(l => l.Attributes).Any(a => SimpleAttributeName(a.Name.ToString()) == shortName);

    /// <summary>Qualified attribute names classify by simple name, with or without the
    /// conventional Attribute suffix.</summary>
    private static string SimpleAttributeName(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name.Substring(dot + 1);
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - "Attribute".Length);
        return name;
    }

    /// <summary>Maps a span inside the fake-class parse back onto the .razor file.</summary>
    private static SourceSpan MapSpan(
        string path, SourceText sourceText, TextSpan spanInEntry, int blockStart)
    {
        var start = spanInEntry.Start - Prefix.Length + blockStart;
        if (start < 0 || start + spanInEntry.Length > sourceText.Length)
            return SourceSpan.None;

        var fileSpan = new TextSpan(start, spanInEntry.Length);
        var lines = sourceText.Lines.GetLinePositionSpan(fileSpan);
        return new SourceSpan(
            path, start, spanInEntry.Length,
            lines.Start.Line, lines.Start.Character, lines.End.Line, lines.End.Character);
    }

    private static IEnumerable<IntermediateNode> Descendants(IntermediateNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    public static string SanitizeIdentifier(string name)
    {
        if (name.Length == 0)
            return "_";
        var sb = new StringBuilder(name.Length + 1);
        if (char.IsDigit(name[0]))
            sb.Append('_');
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.ToString();
    }

    /// <summary>The PascalCase twin a [Reactive] field's state property is named after.</summary>
    public static string ToPascal(string fieldName)
    {
        var trimmed = fieldName.TrimStart('_');
        if (trimmed.Length == 0)
            return "";
        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }
}
