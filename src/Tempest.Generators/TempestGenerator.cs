using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Tempest.Compiler;
using Tempest.Emit;
using Tempest.Parsing;
using Tempest.RazorParser;

namespace Tempest.Generators;

/// <summary>The shell: the only place the pipeline touches Roslyn, files, and build
/// metadata. Components are discovered two ways — each .razor AdditionalFile becomes a
/// file-agnostic <see cref="RazorSource"/> (component name from the file stem, fallback
/// namespace from RootNamespace + the Razor SDK's TargetPath metadata), and each
/// Tempest-attributed member in ordinary C# routes its containing type through the
/// symbol frontend. Both flow through parser → compiler → emitter and come back as
/// generated sources plus Roslyn diagnostics — the shell maps
/// <see cref="TempestDiagnostic"/> spans onto real file locations via the
/// component-to-file association only it knows.</summary>
[Generator]
public sealed class TempestGenerator : IIncrementalGenerator
{
    private static readonly RazorParser.RazorParser Parser = new();
    private static readonly CSharpParser.CSharpParser SymbolParser = new();
    private static readonly TempestCompiler Compiler = new();
    private static readonly TempestEmitter Emitter = new();

    /// <summary>An error escaping the pipeline (a broken input, an unexpected failure)
    /// — a Tempest bug, surfaced instead of swallowed; generation for that file is
    /// skipped.</summary>
    private static readonly DiagnosticDescriptor PipelineError = new(
        id: "TEM900",
        title: "Tempest pipeline error",
        messageFormat: "{0}: {1}",
        category: "Tempest",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var parses = context.AdditionalTextsProvider
            .Where(static file =>
                file.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) &&
                !IsImports(file.Path))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, ct) => ParseFile(pair.Left, pair.Right, ct));

        // Usings from every _Imports.razor, so razor-sourced type names (emitted as
        // written) resolve inside the generated files.
        var imports = context.AdditionalTextsProvider
            .Where(static file => IsImports(file.Path))
            .Select(static (file, ct) => UsingDirectives(file.GetText(ct)?.ToString() ?? ""));

        var symbolParses = SymbolDocuments(context, "Tempest.CommandAttribute", static node => node is MethodDeclarationSyntax)
            .Collect()
            .Combine(SymbolDocuments(context, "Tempest.EventAttribute", static node => node is MethodDeclarationSyntax).Collect())
            .Combine(SymbolDocuments(context, "Tempest.ReactiveAttribute", static node => node is VariableDeclaratorSyntax).Collect())
            .Combine(SymbolDocuments(context, "Tempest.OnChangedAttribute", static node => node is MethodDeclarationSyntax).Collect())
            .Select(static (t, _) => t.Left.Left.Left.AddRange(t.Left.Left.Right).AddRange(t.Left.Right).AddRange(t.Right));

        var all = parses.Collect().Combine(imports.Collect()).Combine(symbolParses);

        context.RegisterSourceOutput(all, static (spc, pair) =>
            Execute(spc, pair.Left.Left.AddRange(pair.Right), pair.Left.Right));
    }

    /// <summary>One parse per attributed member, of the member's whole containing type —
    /// members of one class produce identical documents, which grouping deduplicates.</summary>
    private static IncrementalValuesProvider<SourceParse> SymbolDocuments(
        IncrementalGeneratorInitializationContext context,
        string attributeName,
        Func<SyntaxNode, bool> predicate)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeName,
            (node, _) => predicate(node),
            static (ctx, _) => new SourceParse(
                ctx.TargetNode.SyntaxTree.FilePath,
                SymbolParser.ParseDocument((INamedTypeSymbol)ctx.TargetSymbol.ContainingSymbol!)));

    private static bool IsImports(string path)
        => Path.GetFileName(path).Equals("_Imports.razor", StringComparison.OrdinalIgnoreCase);

    private static SourceParse ParseFile(
        AdditionalText file, AnalyzerConfigOptionsProvider options, CancellationToken ct)
    {
        var text = file.GetText(ct)?.ToString() ?? "";
        var source = new RazorSource(
            ComponentName: Path.GetFileNameWithoutExtension(file.Path),
            Text: text,
            FallbackNamespace: FallbackNamespace(options, file));
        return new SourceParse(file.Path, Parser.ParseDocument(source));
    }

    /// <summary>The namespace when a document has no @namespace directive:
    /// RootNamespace + the project-relative directory. The Razor SDK attaches the
    /// relative path as (base64) TargetPath metadata on the AdditionalFile.</summary>
    private static string FallbackNamespace(AnalyzerConfigOptionsProvider options, AdditionalText file)
    {
        var segments = new List<string>();
        if (options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace) &&
            !string.IsNullOrEmpty(rootNamespace))
            segments.Add(rootNamespace);

        if (options.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.TargetPath", out var encoded) &&
            !string.IsNullOrEmpty(encoded))
        {
            try
            {
                var directory = Path.GetDirectoryName(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                if (!string.IsNullOrEmpty(directory))
                    segments.AddRange(directory!
                        .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(Identifier));
            }
            catch (FormatException)
            {
                // Malformed metadata — fall back to the root namespace alone.
            }
        }

        return string.Join(".", segments);
    }

    /// <summary>A path segment as a valid identifier — the same hygiene the parser
    /// applies to component names.</summary>
    private static string Identifier(string segment)
    {
        var sb = new StringBuilder(segment.Length + 1);
        if (segment.Length > 0 && char.IsDigit(segment[0]))
            sb.Append('_');
        foreach (var c in segment)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    /// <summary>'\n'-joined @using directives of an _Imports.razor.</summary>
    private static string UsingDirectives(string text)
    {
        var directives = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("@using ", StringComparison.Ordinal))
                directives.Add(trimmed.Substring("@using ".Length).Trim().TrimEnd(';').Trim());
        }
        return string.Join("\n", directives);
    }

    private static void Execute(
        SourceProductionContext spc, ImmutableArray<SourceParse> parses, ImmutableArray<string> imports)
    {
        var documents = new List<TempestDocument>();
        var fileByComponent = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parse in parses)
        {
            if (!parse.Result.IsSuccess)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PipelineError, Location.None, parse.Result.Error!.Code, parse.Result.Error.Message));
                continue;
            }

            documents.Add(parse.Result.Value);
            foreach (var name in ComponentNames(parse.Result.Value))
            {
                if (!fileByComponent.ContainsKey(name))
                    fileByComponent.Add(name, parse.Path);
            }
        }

        var compiled = Compiler.Compile(documents, imports);
        if (!compiled.IsSuccess)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                PipelineError, Location.None, compiled.Error!.Code, compiled.Error.Message));
            return;
        }

        foreach (var diagnostic in compiled.Value.Diagnostics)
            spc.ReportDiagnostic(ToRoslyn(diagnostic, fileByComponent));

        foreach (var component in compiled.Value.Components)
        {
            var emitted = Emitter.Emit(component);
            if (!emitted.IsSuccess)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PipelineError, Location.None, emitted.Error!.Code, emitted.Error.Message));
                continue;
            }

            spc.AddSource(emitted.Value.HintName, SourceText.From(emitted.Value.Source, Encoding.UTF8));
        }
    }

    private static IEnumerable<string> ComponentNames(TempestDocument document)
        => document.Commands.Select(m => m.ComponentName)
            .Concat(document.Events.Select(m => m.ComponentName))
            .Concat(document.Reactives.Select(r => r.ComponentName))
            .Concat(document.Hooks.Select(h => h.ComponentName))
            .Distinct(StringComparer.Ordinal);

    private static Diagnostic ToRoslyn(
        TempestDiagnostic diagnostic, IReadOnlyDictionary<string, string> fileByComponent)
    {
        var descriptor = new DiagnosticDescriptor(
            id: diagnostic.Id,
            title: diagnostic.Title,
            messageFormat: "{0}",
            category: "Tempest",
            defaultSeverity: Severity(diagnostic.Severity),
            isEnabledByDefault: true);

        return Diagnostic.Create(descriptor, LocationOf(diagnostic, fileByComponent), diagnostic.Message);
    }

    private static Location LocationOf(
        TempestDiagnostic diagnostic, IReadOnlyDictionary<string, string> fileByComponent)
    {
        if (diagnostic.Span.IsNone || !fileByComponent.TryGetValue(diagnostic.ComponentName, out var path))
            return Location.None;

        return Location.Create(
            path,
            new TextSpan(diagnostic.Span.Start, diagnostic.Span.Length),
            new LinePositionSpan(
                new LinePosition(diagnostic.Span.StartLine, diagnostic.Span.StartColumn),
                new LinePosition(diagnostic.Span.EndLine, diagnostic.Span.EndColumn)));
    }

    private static DiagnosticSeverity Severity(Pipeline.TempestDiagnosticSeverity severity) => severity switch
    {
        Pipeline.TempestDiagnosticSeverity.Error => DiagnosticSeverity.Error,
        Pipeline.TempestDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Info,
    };
}
