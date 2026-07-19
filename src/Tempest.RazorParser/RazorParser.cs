using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Tempest.Model;
using Tempest.Model.Entry;
using Tempest.Parsing;

namespace Tempest.RazorParser;

/// <summary>The .razor frontend of the pipeline: parses the @code blocks of one file into
/// model records. Base types cannot be resolved from text, so any @inherits is trusted
/// (its absence is flagged via InheritsHostBase); type names are carried as written.</summary>
public sealed class RazorParser : IComponentParser<RazorSource>
{
    // Each @code block is parsed as the body of this fake class so member syntax applies.
    private const string Prefix = "class __Razor{";

    public SourceEntries Parse(RazorSource source)
    {
        var text = source.Text;
        if (!text.Contains("[Command") && !text.Contains("[Event") && !text.Contains("[Reactive"))
            return SourceEntries.Empty;

        var componentName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(source.FilePath));
        var ns = ResolveNamespace(text, source.RootNamespace, source.TargetPath);

        // We can't resolve base types from text; trust any @inherits, flag its absence.
        var inherits = Regex.IsMatch(text, @"(^|[^@\w])@inherits\s+\S");

        // Type names are emitted as written, so the generated file needs this file's usings.
        var fileUsings = RazorText.ExtractUsingDirectives(text);

        var sourceText = SourceText.From(text);
        var methods = new List<EntryMethod>();
        var reactiveFields = new List<(string FieldName, string TypeText, bool Valid, string Accessibility, SourceSpan Span)>();
        var hookCandidates = new Dictionary<string, EntryHook>();

        foreach (var (blockStart, blockText) in RazorText.ExtractCodeBlocks(text))
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

                // Hook candidates: On{X}(one parameter) with a body.
                var methodName = method.Identifier.ValueText;
                if (methodName.StartsWith("On", StringComparison.Ordinal) &&
                    method.ParameterList.Parameters.Count == 1 &&
                    (method.Body is not null || method.ExpressionBody is not null) &&
                    !hookCandidates.ContainsKey(methodName))
                {
                    var accessibility = string.Join(" ", method.Modifiers
                        .Where(m => m.IsKind(SyntaxKind.PrivateKeyword) ||
                                    m.IsKind(SyntaxKind.ProtectedKeyword) ||
                                    m.IsKind(SyntaxKind.InternalKeyword) ||
                                    m.IsKind(SyntaxKind.PublicKeyword))
                        .Select(m => m.Text));

                    hookCandidates[methodName] = new EntryHook(
                        Accessibility: accessibility,
                        ReturnsTask: method.ReturnType.ToString() != "void",
                        ParamName: method.ParameterList.Parameters[0].Identifier.ValueText,
                        IsPartial: method.Modifiers.Any(SyntaxKind.PartialKeyword),
                        MethodName: methodName,
                        Span: span);
                }

                var isCommand = false;
                var isEvent = false;
                foreach (var attr in method.AttributeLists.SelectMany(l => l.Attributes))
                {
                    var name = attr.Name.ToString();
                    var dot = name.LastIndexOf('.');
                    if (dot >= 0) name = name.Substring(dot + 1);
                    if (name.EndsWith("Attribute", StringComparison.Ordinal))
                        name = name.Substring(0, name.Length - "Attribute".Length);
                    if (name == "Command") isCommand = true;
                    if (name == "Event") isEvent = true;
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
            hookCandidates.TryGetValue("On" + propertyName + "Changed", out var hook);
            reactives.Add(new EntryReactive(
                Namespace: ns,
                ComponentName: componentName,
                FieldName: fieldName,
                PropertyName: propertyName,
                TypeText: typeText,
                Hook: hook,
                InheritsHostBase: inherits,
                IsValidField: validModifiers && propertyName != fieldName && propertyName.Length > 0,
                Accessibility: accessibility,
                FromRazor: true,
                FileUsings: fileUsings,
                Span: span));
        }

        return new SourceEntries(
            new EquatableArray<EntryMethod>(methods.ToArray()),
            new EquatableArray<EntryReactive>(reactives.ToArray()));
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

    /// <summary>@namespace directive if present, else RootNamespace + the TargetPath's
    /// directory segments (each sanitized), '.'-joined.</summary>
    internal static string ResolveNamespace(string text, string rootNamespace, string targetPath)
    {
        var explicitNs = Regex.Match(text, @"(^|[^@\w])@namespace\s+([A-Za-z_][\w.]*)");
        if (explicitNs.Success)
            return explicitNs.Groups[2].Value;

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
    {
        foreach (var attr in lists.SelectMany(l => l.Attributes))
        {
            var name = attr.Name.ToString();
            var dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(dot + 1);
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Attribute".Length);
            if (name == shortName)
                return true;
        }
        return false;
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
