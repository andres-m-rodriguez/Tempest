using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Tempest.Generators;

/// <summary>
/// Emits one state twin per member: a {Name}State CommandState property per [Command],
/// a {PascalCase}State ReactiveState&lt;T&gt; property (plus optional partial On{PascalCase}Changed
/// hook declaration) per [Reactive] field, and the bus registration override per [Event].
/// Components are discovered two ways: attributed members in ordinary .cs files (via the
/// semantic model), and members inside the @code block of .razor files (parsed from the
/// file text, because the Razor compiler's output is invisible to other generators).
/// </summary>
[Generator]
public sealed class TempestGenerator : IIncrementalGenerator
{
    private const string CommandAttributeName = "Tempest.CommandAttribute";
    private const string EventAttributeName = "Tempest.EventAttribute";
    private const string ReactiveAttributeName = "Tempest.ReactiveAttribute";

    private static readonly DiagnosticDescriptor EventHandlerShape = new(
        id: "TEM001",
        title: "[Event] handler has the wrong shape",
        messageFormat: "[Event] handler '{0}' must take exactly one parameter: a nested record of this component (an [Event, Command] may add a trailing CancellationToken)",
        category: "Tempest",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MustInheritStatefulComponent = new(
        id: "TEM002",
        title: "Component must inherit StatefulComponent",
        messageFormat: "'{0}' uses [Command], [Event] or [Reactive] but does not inherit Tempest.StatefulComponent (or Tempest.StatefulLayoutComponent for layouts)",
        category: "Tempest",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CommandShape = new(
        id: "TEM003",
        title: "[Command] method has the wrong shape",
        messageFormat: "[Command] method '{0}' must be parameterless or take only a trailing CancellationToken, unless it is also an [Event] handler",
        category: "Tempest",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ReactiveFieldShape = new(
        id: "TEM007",
        title: "[Reactive] field has the wrong shape",
        messageFormat: "[Reactive] field '{0}' must be a non-public, non-static field whose name differs from its PascalCase twin",
        category: "Tempest",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor HookNotPartial = new(
        id: "TEM008",
        title: "Reactive hook is not partial",
        messageFormat: "Method '{0}' matches [Reactive] field '{1}' but is not partial; declare it 'partial' to wire it as the change hook",
        category: "Tempest",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context.SyntaxProvider.ForAttributeWithMetadataName(
            CommandAttributeName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => ExtractMethodFromSymbol(ctx));

        var events = context.SyntaxProvider.ForAttributeWithMetadataName(
            EventAttributeName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => ExtractMethodFromSymbol(ctx));

        var reactives = context.SyntaxProvider.ForAttributeWithMetadataName(
            ReactiveAttributeName,
            static (node, _) => node is VariableDeclaratorSyntax,
            static (ctx, _) => ExtractReactiveFromSymbol(ctx));

        var razorMembers = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .SelectMany(static (pair, ct) => ExtractFromRazor(pair.Left, pair.Right, ct));

        // Usings from every _Imports.razor, so razor-sourced type names (emitted as written)
        // resolve inside the generated files.
        var importsUsings = context.AdditionalTextsProvider
            .Where(static f => Path.GetFileName(f.Path).Equals("_Imports.razor", StringComparison.OrdinalIgnoreCase))
            .Select(static (f, ct) => ExtractUsingDirectives(f.GetText(ct)?.ToString() ?? ""));

        var all = commands.Collect()
            .Combine(events.Collect())
            .Combine(reactives.Collect())
            .Combine(razorMembers.Collect())
            .Combine(importsUsings.Collect());

        context.RegisterSourceOutput(all, static (spc, t) => Execute(
            spc,
            t.Left.Left.Left.Left.Concat(t.Left.Left.Left.Right)
                .Concat(t.Left.Right.Select(m => m.Method))
                .OfType<MethodModel>(),
            t.Left.Left.Right.Concat(t.Left.Right.Select(m => m.Reactive)).OfType<ReactiveModel>(),
            t.Right));
    }

    private static string ExtractUsingDirectives(string text)
        => string.Join("\n", Regex.Matches(text, @"(?m)^\s*@using\s+([^\r\n]+)")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value.Trim().TrimEnd(';')));

    private sealed record MethodModel(
        string Namespace,
        string ComponentName,
        string MethodName,
        bool IsCommand,
        bool IsEvent,
        ReturnKind Kind,
        string? ResultType,          // the T of Task<T>/ValueTask<T>, or the sync return type
        bool HasCancellationToken,
        int ParameterCount,          // parameters excluding a trailing CancellationToken
        string? ParamType,
        string? ParamTypeName,
        bool ParamIsNestedRecordOfComponent,
        bool InheritsStatefulComponent,
        string Accessibility,        // of the method — the generated state property matches it
        bool FromRazor,
        string FileUsings,           // '\n'-joined @using directives of the source .razor file
        Location Location);

    private sealed record HookModel(
        string Accessibility,        // as written, e.g. "private" — may be empty
        bool ReturnsTask,
        string ParamName,
        bool IsPartial,
        string MethodName,
        Location Location);

    private sealed record ReactiveModel(
        string Namespace,
        string ComponentName,
        string FieldName,
        string PropertyName,
        string TypeText,
        HookModel? Hook,
        bool InheritsStatefulComponent,
        bool IsValidField,
        string Accessibility,        // of the field — the generated state property matches it
        bool FromRazor,
        string FileUsings,
        Location Location);

    private sealed record RazorMember(MethodModel? Method, ReactiveModel? Reactive);

    private enum ReturnKind { Void, Task, TaskOfT, ValueTask, ValueTaskOfT, Sync }

    // ── Path 1: attributed members in ordinary .cs files ────────────────────

    private static MethodModel? ExtractMethodFromSymbol(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method)
            return null;

        var component = method.ContainingType;

        var isCommand = false;
        var isEvent = false;
        foreach (var attr in method.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name == CommandAttributeName) isCommand = true;
            if (name == EventAttributeName) isEvent = true;
        }

        var (kind, resultType) = ClassifyReturn(method);

        var parameters = method.Parameters;
        var hasCt = parameters.Length > 0 &&
                    parameters[parameters.Length - 1].Type.ToDisplayString() == "System.Threading.CancellationToken";
        var effective = hasCt ? parameters.Length - 1 : parameters.Length;

        string? paramType = null;
        string? paramTypeName = null;
        var paramIsNestedRecord = false;
        if (effective == 1 && parameters[0].Type is INamedTypeSymbol pt)
        {
            paramType = pt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            paramTypeName = pt.Name;
            paramIsNestedRecord = pt.IsRecord &&
                SymbolEqualityComparer.Default.Equals(pt.ContainingType, component);
        }

        return new MethodModel(
            Namespace: component.ContainingNamespace.IsGlobalNamespace
                ? ""
                : component.ContainingNamespace.ToDisplayString(),
            ComponentName: component.Name,
            MethodName: method.Name,
            IsCommand: isCommand,
            IsEvent: isEvent,
            Kind: kind,
            ResultType: resultType,
            HasCancellationToken: hasCt,
            ParameterCount: effective,
            ParamType: paramType,
            ParamTypeName: paramTypeName,
            ParamIsNestedRecordOfComponent: paramIsNestedRecord,
            InheritsStatefulComponent: InheritsStateful(component),
            Accessibility: AccessibilityText(method.DeclaredAccessibility),
            FromRazor: false,
            FileUsings: "",
            Location: ctx.TargetNode.GetLocation());
    }

    private static ReactiveModel? ExtractReactiveFromSymbol(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IFieldSymbol field)
            return null;

        var component = field.ContainingType;
        var propertyName = ToPascal(field.Name);
        var isValid = field.DeclaredAccessibility != Accessibility.Public &&
                      !field.IsStatic &&
                      propertyName != field.Name &&
                      propertyName.Length > 0;

        HookModel? hook = null;
        var hookName = "On" + propertyName + "Changed";
        var candidate = component.GetMembers(hookName).OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary && m.Parameters.Length == 1);
        if (candidate is not null)
        {
            var isPartial = false;
            var hasBody = false;
            foreach (var reference in candidate.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is MethodDeclarationSyntax syntax)
                {
                    if (syntax.Modifiers.Any(SyntaxKind.PartialKeyword)) isPartial = true;
                    if (syntax.Body is not null || syntax.ExpressionBody is not null) hasBody = true;
                }
            }

            if (hasBody)
                hook = new HookModel(
                    Accessibility: AccessibilityText(candidate.DeclaredAccessibility),
                    ReturnsTask: !candidate.ReturnsVoid,
                    ParamName: candidate.Parameters[0].Name,
                    IsPartial: isPartial,
                    MethodName: hookName,
                    Location: candidate.Locations.FirstOrDefault() ?? ctx.TargetNode.GetLocation());
        }

        return new ReactiveModel(
            Namespace: component.ContainingNamespace.IsGlobalNamespace
                ? ""
                : component.ContainingNamespace.ToDisplayString(),
            ComponentName: component.Name,
            FieldName: field.Name,
            PropertyName: propertyName,
            TypeText: field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Hook: hook,
            InheritsStatefulComponent: InheritsStateful(component),
            IsValidField: isValid,
            Accessibility: AccessibilityText(field.DeclaredAccessibility),
            FromRazor: false,
            FileUsings: "",
            Location: ctx.TargetNode.GetLocation());
    }

    private static (ReturnKind Kind, string? ResultType) ClassifyReturn(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
            return (ReturnKind.Void, null);

        const string TaskName = "global::System.Threading.Tasks.Task";
        const string ValueTaskName = "global::System.Threading.Tasks.ValueTask";
        var display = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (display == TaskName)
            return (ReturnKind.Task, null);
        if (display.StartsWith(TaskName + "<", StringComparison.Ordinal))
            return (ReturnKind.TaskOfT, Inner(display, TaskName.Length + 1));
        if (display == ValueTaskName)
            return (ReturnKind.ValueTask, null);
        if (display.StartsWith(ValueTaskName + "<", StringComparison.Ordinal))
            return (ReturnKind.ValueTaskOfT, Inner(display, ValueTaskName.Length + 1));
        return (ReturnKind.Sync, display);

        static string Inner(string s, int start) => s.Substring(start, s.Length - start - 1);
    }

    /// <summary>Textual counterpart of ClassifyReturn for methods parsed out of .razor files.</summary>
    private static (ReturnKind Kind, string? ResultType) ClassifyReturnText(string rt)
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

    private static bool InheritsStateful(INamedTypeSymbol component)
    {
        for (var baseType = component.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var display = baseType.ToDisplayString();
            if (display is "Tempest.StatefulComponent" or "Tempest.StatefulLayoutComponent")
                return true;
        }
        return false;
    }

    private static string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "private",
    };

    // ── Path 2: members inside the @code block of .razor files ──────────────

    private static IEnumerable<RazorMember> ExtractFromRazor(
        AdditionalText file, AnalyzerConfigOptionsProvider optionsProvider, CancellationToken ct)
    {
        var sourceText = file.GetText(ct);
        if (sourceText is null)
            return [];

        var text = sourceText.ToString();
        if (!text.Contains("[Command") && !text.Contains("[Event") && !text.Contains("[Reactive"))
            return [];

        var componentName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(file.Path));
        var ns = ResolveNamespace(text, file, optionsProvider);

        // We can't resolve base types from text; trust any @inherits, flag its absence.
        var inherits = Regex.IsMatch(text, @"(^|[^@\w])@inherits\s+\S");

        // Type names are emitted as written, so the generated file needs this file's usings.
        var fileUsings = ExtractUsingDirectives(text);

        var results = new List<RazorMember>();
        var reactiveFields = new List<(string FieldName, string TypeText, bool Valid, string Accessibility, Location Location)>();
        var hookCandidates = new Dictionary<string, HookModel>();
        const string Prefix = "class __Razor{";

        foreach (var (blockStart, blockText) in ExtractCodeBlocks(text))
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
                        MapRazorLocation(file.Path, sourceText, variable.Identifier.Span, Prefix.Length, blockStart)));
            }

            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                var location = MapRazorLocation(
                    file.Path, sourceText, method.Identifier.Span, Prefix.Length, blockStart);

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

                    hookCandidates[methodName] = new HookModel(
                        Accessibility: accessibility,
                        ReturnsTask: method.ReturnType.ToString() != "void",
                        ParamName: method.ParameterList.Parameters[0].Identifier.ValueText,
                        IsPartial: method.Modifiers.Any(SyntaxKind.PartialKeyword),
                        MethodName: methodName,
                        Location: location);
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

                results.Add(new RazorMember(new MethodModel(
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
                    InheritsStatefulComponent: inherits,
                    Accessibility: AccessibilityModifierText(method.Modifiers),
                    FromRazor: true,
                    FileUsings: fileUsings,
                    Location: location), null));
            }
        }

        foreach (var (fieldName, typeText, validModifiers, accessibility, location) in reactiveFields)
        {
            var propertyName = ToPascal(fieldName);
            hookCandidates.TryGetValue("On" + propertyName + "Changed", out var hook);
            results.Add(new RazorMember(null, new ReactiveModel(
                Namespace: ns,
                ComponentName: componentName,
                FieldName: fieldName,
                PropertyName: propertyName,
                TypeText: typeText,
                Hook: hook,
                InheritsStatefulComponent: inherits,
                IsValidField: validModifiers && propertyName != fieldName && propertyName.Length > 0,
                Accessibility: accessibility,
                FromRazor: true,
                FileUsings: fileUsings,
                Location: location)));
        }

        return results;
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

    private static string ResolveNamespace(
        string text, AdditionalText file, AnalyzerConfigOptionsProvider optionsProvider)
    {
        var explicitNs = Regex.Match(text, @"(^|[^@\w])@namespace\s+([A-Za-z_][\w.]*)");
        if (explicitNs.Success)
            return explicitNs.Groups[2].Value;

        var segments = new List<string>();
        if (optionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNs) &&
            !string.IsNullOrEmpty(rootNs))
            segments.Add(rootNs);

        // The Razor SDK attaches the project-relative path as (base64) TargetPath metadata.
        if (optionsProvider.GetOptions(file)
                .TryGetValue("build_metadata.AdditionalFiles.TargetPath", out var encoded) &&
            !string.IsNullOrEmpty(encoded))
        {
            string targetPath;
            try
            {
                targetPath = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                targetPath = encoded;
            }

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
                segments.AddRange(dir
                    .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizeIdentifier));
        }

        return string.Join(".", segments);
    }

    /// <summary>Finds each `@code { ... }` block; yields (content offset in file, content).</summary>
    private static IEnumerable<(int Start, string Text)> ExtractCodeBlocks(string text)
    {
        var i = 0;
        while ((i = text.IndexOf("@code", i, StringComparison.Ordinal)) >= 0)
        {
            var after = i + "@code".Length;
            var boundedBefore = i == 0 ||
                (!char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_' && text[i - 1] != '@');

            var j = after;
            while (j < text.Length && char.IsWhiteSpace(text[j]))
                j++;

            if (!boundedBefore || j >= text.Length || text[j] != '{')
            {
                i = after;
                continue;
            }

            var close = FindMatchingBrace(text, j);
            if (close < 0)
                yield break;

            yield return (j + 1, text.Substring(j + 1, close - j - 1));
            i = close + 1;
        }
    }

    /// <summary>Brace matching that skips comments and string/char literals.</summary>
    private static int FindMatchingBrace(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    if (--depth == 0) return i;
                    break;
                case '/':
                    if (i + 1 < s.Length && s[i + 1] == '/')
                    {
                        i = s.IndexOf('\n', i);
                        if (i < 0) return -1;
                    }
                    else if (i + 1 < s.Length && s[i + 1] == '*')
                    {
                        i = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        if (i < 0) return -1;
                        i++;
                    }
                    break;
                case '"':
                {
                    var verbatim = i > open && (s[i - 1] == '@' ||
                        (s[i - 1] == '$' && i >= 2 && s[i - 2] == '@'));
                    i = SkipString(s, i, verbatim);
                    if (i < 0) return -1;
                    break;
                }
                case '\'':
                    i++;
                    if (i < s.Length && s[i] == '\\') i++;
                    i++;
                    break;
            }
        }
        return -1;
    }

    private static int SkipString(string s, int openQuote, bool verbatim)
    {
        for (var i = openQuote + 1; i < s.Length; i++)
        {
            if (s[i] == '\\' && !verbatim)
            {
                i++;
            }
            else if (s[i] == '"')
            {
                if (verbatim && i + 1 < s.Length && s[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                return i;
            }
        }
        return -1;
    }

    private static Location MapRazorLocation(
        string path, SourceText sourceText, TextSpan spanInParsed, int prefixLength, int blockStart)
    {
        var start = spanInParsed.Start - prefixLength + blockStart;
        if (start < 0 || start + spanInParsed.Length > sourceText.Length)
            return Location.None;

        var fileSpan = new TextSpan(start, spanInParsed.Length);
        return Location.Create(path, fileSpan, sourceText.Lines.GetLinePositionSpan(fileSpan));
    }

    private static string SanitizeIdentifier(string name)
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

    private static string ToPascal(string fieldName)
    {
        var trimmed = fieldName.TrimStart('_');
        if (trimmed.Length == 0)
            return "";
        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }

    // ── Generation ───────────────────────────────────────────────────────────

    private static void Execute(
        SourceProductionContext spc,
        IEnumerable<MethodModel> methodModels,
        IEnumerable<ReactiveModel> reactiveModels,
        ImmutableArray<string> importsUsings)
    {
        // A .cs method with [Event, Command] is found by both symbol providers; keep one copy.
        var methods = methodModels
            .GroupBy(m => (m.Namespace, m.ComponentName, m.MethodName, m.ParamType))
            .Select(g => g.First());

        var reactives = reactiveModels
            .GroupBy(r => (r.Namespace, r.ComponentName, r.FieldName))
            .Select(g => g.First());

        var components = methods
            .Select(m => (m.Namespace, m.ComponentName))
            .Concat(reactives.Select(r => (r.Namespace, r.ComponentName)))
            .Distinct();

        var methodsByComponent = methods.ToLookup(m => (m.Namespace, m.ComponentName));
        var reactivesByComponent = reactives.ToLookup(r => (r.Namespace, r.ComponentName));

        foreach (var key in components)
        {
            var validMethods = new List<MethodModel>();
            var validReactives = new List<ReactiveModel>();
            var componentOk = true;

            foreach (var m in methodsByComponent[key])
            {
                if (!m.InheritsStatefulComponent)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        MustInheritStatefulComponent, m.Location, m.ComponentName));
                    componentOk = false;
                    continue;
                }

                if (m.IsEvent &&
                    (m.ParameterCount != 1 ||
                     !m.ParamIsNestedRecordOfComponent ||
                     (m.HasCancellationToken && !m.IsCommand)))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        EventHandlerShape, m.Location, m.MethodName));
                    continue;
                }

                if (m.IsCommand && !m.IsEvent && m.ParameterCount != 0)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        CommandShape, m.Location, m.MethodName));
                    continue;
                }

                validMethods.Add(m);
            }

            foreach (var r in reactivesByComponent[key])
            {
                if (!r.InheritsStatefulComponent)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        MustInheritStatefulComponent, r.Location, r.ComponentName));
                    componentOk = false;
                    continue;
                }

                if (!r.IsValidField)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        ReactiveFieldShape, r.Location, r.FieldName));
                    continue;
                }

                if (r.Hook is { IsPartial: false } lateHook)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        HookNotPartial, lateHook.Location, lateHook.MethodName, r.FieldName));
                    validReactives.Add(r with { Hook = null });
                    continue;
                }

                validReactives.Add(r);
            }

            if (!componentOk || (validMethods.Count == 0 && validReactives.Count == 0))
                continue;

            // Razor-sourced members carry type names as written; give the generated file
            // the source file's usings plus every _Imports.razor's usings so they resolve.
            var extraUsings = new SortedSet<string>(StringComparer.Ordinal);
            if (validMethods.Any(m => m.FromRazor) || validReactives.Any(r => r.FromRazor))
            {
                foreach (var joined in importsUsings)
                    AddUsings(extraUsings, joined);
                foreach (var m in validMethods)
                    AddUsings(extraUsings, m.FileUsings);
                foreach (var r in validReactives)
                    AddUsings(extraUsings, r.FileUsings);
            }

            var source = GenerateComponent(key.Namespace, key.ComponentName, validMethods, validReactives, extraUsings);
            var hint = key.Namespace.Length == 0
                ? $"{key.ComponentName}.Tempest.g.cs"
                : $"{key.Namespace}.{key.ComponentName}.Tempest.g.cs";
            spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static void AddUsings(SortedSet<string> target, string joined)
    {
        foreach (var directive in joined.Split('\n'))
        {
            if (directive.Length > 0 && !StandardUsings.Contains(directive))
                target.Add(directive);
        }
    }

    private static readonly ImmutableHashSet<string> StandardUsings = ImmutableHashSet.Create(
        "System", "System.Collections.Generic", "System.Threading", "System.Threading.Tasks");

    private static string GenerateComponent(
        string ns, string componentName, List<MethodModel> methods, List<ReactiveModel> reactives,
        IReadOnlyCollection<string> extraUsings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Tempest />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        // Members parsed out of .razor files carry their type names as written, so give
        // the generated file the usings those names rely on.
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        foreach (var directive in extraUsings)
            sb.AppendLine($"using {directive};");
        sb.AppendLine();

        var indent = "";
        if (ns.Length > 0)
        {
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            indent = "    ";
        }

        sb.AppendLine($"{indent}partial class {componentName}");
        sb.AppendLine($"{indent}{{");

        var body = indent + "    ";
        const string TaskType = "global::System.Threading.Tasks.Task";

        foreach (var m in methods.Where(m => m.IsCommand))
        {
            // Plain [Command]: named after the method. [Event, Command]: named after the record.
            var name = m.IsEvent ? m.ParamTypeName! : m.MethodName;
            var backing = $"__{char.ToLowerInvariant(name[0])}{name.Substring(1)}State";

            // Result-bearing return types get a CommandState<TResult>; event commands stay
            // resultless (the bus publisher has no way to receive a value anyway).
            var stateType = m.IsEvent
                ? $"global::Tempest.EventCommandState<{m.ParamType}>"
                : m.Kind is ReturnKind.TaskOfT or ReturnKind.ValueTaskOfT or ReturnKind.Sync
                    ? $"global::Tempest.CommandState<{m.ResultType}>"
                    : "global::Tempest.CommandState";

            var args = m.IsEvent
                ? (m.HasCancellationToken ? "__e, __ct" : "__e")
                : (m.HasCancellationToken ? "__ct" : "");
            var lambdaParams = m.IsEvent ? "(__e, __ct)" : "__ct";
            var call = $"{m.MethodName}({args})";
            var command = m.Kind switch
            {
                ReturnKind.Task or ReturnKind.TaskOfT => $"{lambdaParams} => {call}",
                ReturnKind.ValueTask or ReturnKind.ValueTaskOfT => $"{lambdaParams} => {call}.AsTask()",
                ReturnKind.Sync when !m.IsEvent => $"{lambdaParams} => {TaskType}.FromResult({call})",
                _ => $"{lambdaParams} => {{ {call}; return {TaskType}.CompletedTask; }}",
            };

            sb.AppendLine($"{body}private {stateType}? {backing};");
            sb.AppendLine();
            sb.AppendLine($"{body}/// <summary>State for the {name} command: IsLoading, Error, Execute, TryExecute.</summary>");
            sb.AppendLine($"{body}{m.Accessibility} {stateType} {name}State => {backing} ??= new {stateType}(this, {command});");
            sb.AppendLine();
        }

        foreach (var r in reactives)
        {
            var backing = $"__{char.ToLowerInvariant(r.PropertyName[0])}{r.PropertyName.Substring(1)}State";
            var stateType = $"global::Tempest.ReactiveState<{r.TypeText}>";

            string hookArgument;
            if (r.Hook is null)
            {
                hookArgument = "null";
            }
            else
            {
                // Defining half of the user's partial hook implementation.
                var accessibility = r.Hook.Accessibility.Length > 0 ? r.Hook.Accessibility + " " : "";
                var returnType = r.Hook.ReturnsTask ? TaskType : "void";
                sb.AppendLine($"{body}{accessibility}partial {returnType} {r.Hook.MethodName}({r.TypeText} {r.Hook.ParamName});");
                sb.AppendLine();

                hookArgument = r.Hook.ReturnsTask
                    ? $"__v => {r.Hook.MethodName}(__v)"
                    : $"__v => {{ {r.Hook.MethodName}(__v); return {TaskType}.CompletedTask; }}";
            }

            sb.AppendLine($"{body}private {stateType}? {backing};");
            sb.AppendLine();
            sb.AppendLine($"{body}/// <summary>State for the {r.FieldName} field: Value, SetSilently, IsDirty, Reset.</summary>");
            sb.AppendLine($"{body}{r.Accessibility} {stateType} {r.PropertyName}State => {backing} ??= new {stateType}(");
            sb.AppendLine($"{body}    this,");
            sb.AppendLine($"{body}    () => {r.FieldName},");
            sb.AppendLine($"{body}    __v => {r.FieldName} = __v,");
            sb.AppendLine($"{body}    {r.FieldName},");
            sb.AppendLine($"{body}    {hookArgument});");
            sb.AppendLine();
        }

        var events = methods.Where(m => m.IsEvent).ToList();
        if (events.Count > 0 || reactives.Count > 0)
        {
            sb.AppendLine($"{body}protected override void RegisterTempestHandlers(global::Tempest.IEventBus bus)");
            sb.AppendLine($"{body}{{");
            foreach (var m in events)
            {
                var subscription = m switch
                {
                    // Bus-triggered commands run through TryExecute so a publish can't
                    // blow up in the publisher; the error lands in {Name}State.Error.
                    { IsCommand: true } =>
                        $"SubscribeEvent<{m.ParamType}>(e => InvokeAsync(() => {m.ParamTypeName}State.TryExecute(e)));",
                    { Kind: ReturnKind.Task or ReturnKind.TaskOfT } =>
                        $"SubscribeEvent<{m.ParamType}>(e => DispatchEvent(() => {m.MethodName}(e)));",
                    { Kind: ReturnKind.ValueTask or ReturnKind.ValueTaskOfT } =>
                        $"SubscribeEvent<{m.ParamType}>(e => DispatchEvent(() => {m.MethodName}(e).AsTask()));",
                    _ =>
                        $"SubscribeEvent<{m.ParamType}>(e => DispatchEvent(() => {{ {m.MethodName}(e); }}));",
                };
                sb.AppendLine($"{body}    {subscription}");
            }
            foreach (var r in reactives)
            {
                // Touch each reactive state so Initial captures the field's initializer value.
                sb.AppendLine($"{body}    _ = {r.PropertyName}State;");
            }
            sb.AppendLine($"{body}}}");
        }

        sb.AppendLine($"{indent}}}");
        if (ns.Length > 0)
            sb.AppendLine("}");

        return sb.ToString();
    }
}
