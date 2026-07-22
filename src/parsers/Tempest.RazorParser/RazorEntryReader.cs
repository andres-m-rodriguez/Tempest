using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.RazorParser;

/// <summary>The frontend's orchestrator: one pass over a <see cref="RazorSource"/> —
/// <see cref="RazorTokenizer"/> indexes the document, <see cref="RazorMarkupParser"/>
/// assembles its directives and code blocks, <see cref="CSharpSyntaxService"/> reads
/// each block's members, and the entries are assembled with hooks resolved onto their
/// fields. Base types cannot be resolved from text, so @inherits is matched by simple
/// name against the Tempest host bases; type names are carried as written, which is why
/// every entry sets NeedsAmbientUsings.</summary>
internal sealed class RazorEntryReader
{
    private readonly RazorTokenizer _tokenizer = new();
    private readonly RazorMarkupParser _markup = new();
    private readonly HostResolutionService _host = new();
    private readonly CSharpSyntaxService _syntax = new();
    private readonly SanitizeService _sanitize = new();

    internal Result<TempestDocument> Read(RazorSource? source)
    {
        if (InvalidRazorSourceError.Check(source) is { } invalid)
            return Result<TempestDocument>.Fail(invalid);

        try
        {
            return Result<TempestDocument>.Ok(ReadCore(source!));
        }
        catch (Exception ex)
        {
            return Result<TempestDocument>.Fail(
                new RazorEngineError(source!.ComponentName, ex.GetType().Name, ex.Message));
        }
    }

    private TempestDocument ReadCore(RazorSource source)
    {
        var text = source.Text;
        if (!text.Contains("[Command") && !text.Contains("[Event") &&
            !text.Contains("[Reactive") && !text.Contains("[OnChanged"))
            return TempestDocument.Empty;

        var componentName = _sanitize.SanitizeIdentifier(source.ComponentName);

        var document = _markup.Parse(_tokenizer.Tokenize(text.AsSpan()));

        string? namespaceDirective = null;
        string? inherits = null;
        var usings = new List<string>();
        foreach (var directive in document.OfType<DirectiveNode>())
        {
            switch (directive.Name)
            {
                case "namespace": namespaceDirective ??= directive.Value; break;
                case "inherits": inherits ??= directive.Value; break;
                case "using": usings.Add(directive.Value.TrimEnd(';').Trim()); break;
            }
        }

        var ns = namespaceDirective is { Length: > 0 } content ? content : source.FallbackNamespace;
        var host = _host.Resolve(inherits);
        var fileUsings = string.Join("\n", usings);

        var sourceText = SourceText.From(text);
        var methods = new List<SourceMethod>();
        var reactiveFields = new List<(string FieldName, string TypeText, bool Valid, string Accessibility, SourceSpan Span)>();
        var hooks = new List<SourceHook>();

        foreach (var block in document.OfType<CodeBlockNode>())
        {
            var blockStart = block.ContentStart;
            var blockText = block.ContentSlice(text.AsSpan());
            if (_syntax.ParseFakeClass(blockText) is not { } cls)
                continue;

            var recordNames = cls.Members
                .OfType<RecordDeclarationSyntax>()
                .Select(r => r.Identifier.ValueText)
                .ToImmutableHashSet();

            foreach (var fld in cls.Members.OfType<FieldDeclarationSyntax>())
            {
                if (!_syntax.HasAttribute(fld.AttributeLists, "Reactive"))
                    continue;

                var badModifiers =
                    fld.Modifiers.Any(SyntaxKind.StaticKeyword) ||
                    fld.Modifiers.Any(SyntaxKind.PublicKeyword);
                var fieldAccessibility = _syntax.AccessibilityModifierText(fld.Modifiers, defaultTo: "private");

                foreach (var variable in fld.Declaration.Variables)
                    reactiveFields.Add((
                        variable.Identifier.ValueText,
                        fld.Declaration.Type.ToString(),
                        !badModifiers,
                        fieldAccessibility,
                        _syntax.MapSpan(sourceText, variable.Identifier.Span, blockStart)));
            }

            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                var span = _syntax.MapSpan(sourceText, method.Identifier.Span, blockStart);
                var methodName = method.Identifier.ValueText;

                var isCommand = false;
                var isEvent = false;
                AttributeSyntax? onChanged = null;
                foreach (var attr in method.AttributeLists.SelectMany(l => l.Attributes))
                {
                    var name = _syntax.SimpleAttributeName(attr.Name.ToString());
                    if (name == "Command") isCommand = true;
                    if (name == "Event") isEvent = true;
                    if (name == "OnChanged") onChanged ??= attr;
                }

                if (onChanged is not null)
                {
                    hooks.Add(new SourceHook(
                        Namespace: ns,
                        ComponentName: componentName,
                        MethodName: methodName,
                        ExplicitTarget: _syntax.ExplicitHookTarget(onChanged),
                        ReturnsTask: method.ReturnType.ToString() != "void",
                        ParamName: method.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText ?? "",
                        ParameterCount: method.ParameterList.Parameters.Count,
                        Accessibility: _syntax.AccessibilityModifierText(method.Modifiers, defaultTo: ""),
                        Span: span));
                }

                if (!isCommand && !isEvent)
                    continue;

                var (kind, resultType) = _syntax.ClassifyReturnText(method.ReturnType.ToString());

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

                methods.Add(new SourceMethod(
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
                    Host: host,
                    Accessibility: _syntax.AccessibilityModifierText(method.Modifiers, defaultTo: "private"),
                    NeedsAmbientUsings: true,
                    FileUsings: fileUsings,
                    Span: span));
            }
        }

        var reactives = new List<SourceReactiveProperty>();
        foreach (var (fieldName, typeText, validModifiers, accessibility, span) in reactiveFields)
        {
            var propertyName = _sanitize.ToPascal(fieldName);
            reactives.Add(new SourceReactiveProperty(
                Namespace: ns,
                ComponentName: componentName,
                FieldName: fieldName,
                PropertyName: propertyName,
                TypeText: typeText,
                Host: host,
                IsValidField: validModifiers && propertyName != fieldName && propertyName.Length > 0,
                Accessibility: accessibility,
                NeedsAmbientUsings: true,
                FileUsings: fileUsings,
                Span: span));
        }

        return new TempestDocument(
            Commands: new EquatableArray<SourceMethod>(methods.Where(m => m.IsCommand).ToArray()),
            Events: new EquatableArray<SourceMethod>(methods.Where(m => m.IsEvent).ToArray()),
            Reactives: new EquatableArray<SourceReactiveProperty>(reactives.ToArray()),
            Hooks: new EquatableArray<SourceHook>(hooks.ToArray()));
    }
}
