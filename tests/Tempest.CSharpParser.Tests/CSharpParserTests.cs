using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tempest.Parsing;
using Xunit;

namespace Tempest.CSharpParser.Tests;

public class CSharpParserTests
{
    private static readonly CSharpParser Parser = new();

    // The frontend matches attributes and host bases by name, so stubs declared in the
    // test compilation stand in for the real runtime.
    private const string Stubs = """
        namespace Tempest
        {
            public sealed class CommandAttribute : System.Attribute { }
            public sealed class EventAttribute : System.Attribute { }
            public sealed class ReactiveAttribute : System.Attribute { }
            public sealed class OnChangedAttribute : System.Attribute
            {
                public OnChangedAttribute() { }
                public OnChangedAttribute(string target) { }
            }
            public sealed class CanExecuteAttribute : System.Attribute
            {
                public CanExecuteAttribute() { }
                public CanExecuteAttribute(string target) { }
            }
            public abstract class StatefulComponent { }
            public abstract class StatefulControl { }
            public abstract class StatefulPage { }
            public abstract class StatefulStore { }
        }
        """;

    private static INamedTypeSymbol Component(string source, string metadataName = "App.C")
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText(source)],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var symbol = compilation.GetTypeByMetadataName(metadataName);
        Assert.NotNull(symbol);
        return symbol;
    }

    private static IReadOnlyList<MetadataReference> References()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(MetadataReference (p) => MetadataReference.CreateFromFile(p))
            .ToList();

    private static TempestDocument Parse(string source)
    {
        var result = Parser.ParseDocument(Component(source));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void CommandsClassifyReturnsAndCancellation()
    {
        var document = Parse("""
            using System.Threading;
            using System.Threading.Tasks;
            namespace App;
            public sealed partial class C : Tempest.StatefulComponent
            {
                [Tempest.Command] private Task Save(CancellationToken ct) => Task.CompletedTask;
                [Tempest.Command] private Task<int> Count() => Task.FromResult(0);
                [Tempest.Command] protected int Total() => 0;
                [Tempest.Command] private void Clear() { }
            }
            """);

        var save = Assert.Single(document.Commands, m => m.MethodName == "Save");
        Assert.Equal(ReturnKind.Task, save.Kind);
        Assert.True(save.HasCancellationToken);
        Assert.Equal(0, save.ParameterCount);
        Assert.Equal(HostKind.Component, save.Host);
        Assert.Equal("App", save.Namespace);
        Assert.False(save.NeedsAmbientUsings);
        Assert.Equal("", save.FileUsings);

        Assert.Equal((ReturnKind.TaskOfT, "int"), Assert.Single(document.Commands, m => m.MethodName == "Count") is { } c ? (c.Kind, c.ResultType) : default);
        Assert.Equal((ReturnKind.Sync, "int", "protected"), Assert.Single(document.Commands, m => m.MethodName == "Total") is { } t ? (t.Kind, t.ResultType, t.Accessibility) : default);
        Assert.Equal(ReturnKind.Void, Assert.Single(document.Commands, m => m.MethodName == "Clear").Kind);
    }

    [Fact]
    public void EventHandlerSeesNestedRecordFullyQualified()
    {
        var document = Parse("""
            namespace App;
            public sealed partial class C : Tempest.StatefulComponent
            {
                public sealed record ItemAdded(int Id);
                [Tempest.Event] private void OnItemAdded(ItemAdded e) { }
            }
            """);

        var handler = Assert.Single(document.Events);
        Assert.True(handler.ParamIsNestedRecordOfComponent);
        Assert.Equal("global::App.C.ItemAdded", handler.ParamType);
        Assert.Equal("ItemAdded", handler.ParamTypeName);
    }

    [Fact]
    public void ReactiveFieldsJudgeValidityAndQualifyTypes()
    {
        var document = Parse("""
            using System.Collections.Generic;
            namespace App;
            public sealed partial class C : Tempest.StatefulComponent
            {
                [Tempest.Reactive] private List<string> _items = [];
                [Tempest.Reactive] public string _bad = "";
                [Tempest.Reactive] private static string _worse = "";
            }
            """);

        Assert.Equal(3, document.Reactives.Count);
        var items = Assert.Single(document.Reactives, r => r.FieldName == "_items");
        Assert.True(items.IsValidField);
        Assert.Equal("Items", items.PropertyName);
        Assert.Equal("global::System.Collections.Generic.List<string>", items.TypeText);
        Assert.False(Assert.Single(document.Reactives, r => r.FieldName == "_bad").IsValidField);
        Assert.False(Assert.Single(document.Reactives, r => r.FieldName == "_worse").IsValidField);
    }

    [Fact]
    public void HooksCarryExplicitTargetsFromTheAttribute()
    {
        var document = Parse("""
            using System.Threading.Tasks;
            namespace App;
            public sealed partial class C : Tempest.StatefulComponent
            {
                [Tempest.Reactive] private int _count;
                [Tempest.OnChanged] private void OnCountChanged(int value) { }
                [Tempest.OnChanged("_count")] private async Task Bumped(int v) => await Task.Yield();
            }
            """);

        Assert.Equal(2, document.Hooks.Count);
        var bare = Assert.Single(document.Hooks, h => h.MethodName == "OnCountChanged");
        Assert.Null(bare.ExplicitTarget);
        Assert.Equal("value", bare.ParamName);
        Assert.False(bare.ReturnsTask);

        var targeted = Assert.Single(document.Hooks, h => h.MethodName == "Bumped");
        Assert.Equal("_count", targeted.ExplicitTarget);
        Assert.True(targeted.ReturnsTask);
        Assert.Equal(1, targeted.ParameterCount);
    }

    [Fact]
    public void CanExecuteMembersAreCollectedFromPropertiesAndMethods()
    {
        var document = Parse("""
            namespace App;
            public sealed partial class C : Tempest.StatefulComponent
            {
                [Tempest.Command] private void Next() { }
                [Tempest.CanExecute] public bool CanNext { get; private set; }
                [Tempest.CanExecute(nameof(Next))] private bool HasSession() => true;
                [Tempest.CanExecute] public int NotABool { get; set; }
            }
            """);

        Assert.Equal(3, document.CanExecutes.Count);

        var property = Assert.Single(document.CanExecutes, c => c.MemberName == "CanNext");
        Assert.Null(property.ExplicitTarget);
        Assert.True(property.ReturnsBool);
        Assert.False(property.IsMethod);

        var method = Assert.Single(document.CanExecutes, c => c.MemberName == "HasSession");
        Assert.Equal("Next", method.ExplicitTarget);
        Assert.True(method.IsMethod);
        Assert.Equal(0, method.ParameterCount);

        Assert.False(Assert.Single(document.CanExecutes, c => c.MemberName == "NotABool").ReturnsBool);
    }

    [Fact]
    public void HostWalksTheFullBaseChain()
    {
        var store = Parse("""
            namespace App;
            public abstract class MyStoreBase : Tempest.StatefulStore { }
            public sealed partial class C : MyStoreBase
            {
                [Tempest.Command] private void Go() { }
            }
            """);
        Assert.Equal(HostKind.Store, Assert.Single(store.Commands).Host);

        var none = Parse("""
            namespace App;
            public sealed partial class C
            {
                [Tempest.Command] private void Go() { }
            }
            """);
        Assert.Equal(HostKind.None, Assert.Single(none.Commands).Host);
    }

    [Fact]
    public void PrimaryConstructorBecomesInjection()
    {
        var document = Parse("""
            namespace App;
            public interface IThing { }
            public sealed partial class C(IThing thing, string name) : Tempest.StatefulPage
            {
                [Tempest.Command] private void Go() { }
            }
            """);

        var injection = Assert.Single(document.Injections);
        Assert.Equal(HostKind.Control, injection.Host);
        Assert.Equal(
            ["global::App.IThing", "string"],
            injection.ParameterTypes.ToArray());
    }

    [Fact]
    public void ExplicitParameterlessOrAmbiguousConstructorsProduceNoInjection()
    {
        var parameterless = Parse("""
            namespace App;
            public sealed partial class C : Tempest.StatefulPage
            {
                public C() { }
                [Tempest.Command] private void Go() { }
            }
            """);
        Assert.Empty(parameterless.Injections);

        var overloaded = Parse("""
            namespace App;
            public interface IThing { }
            public sealed partial class C : Tempest.StatefulPage
            {
                public C(IThing thing) { }
                public C(IThing thing, int extra) { }
                [Tempest.Command] private void Go() { }
            }
            """);
        Assert.Empty(overloaded.Injections);
    }

    [Fact]
    public void StatefulPageResolvesAsControlHost()
    {
        var page = Parse("""
            namespace App;
            public sealed partial class C : Tempest.StatefulPage
            {
                [Tempest.Command] private void Go() { }
            }
            """);
        Assert.Equal(HostKind.Control, Assert.Single(page.Commands).Host);
    }

    [Fact]
    public void NullSymbolFailsWithError()
    {
        var result = Parser.ParseDocument(null!);

        Assert.False(result.IsSuccess);
        Assert.Equal("CSP001", Assert.IsType<InvalidSymbolSourceError>(result.Error).Code);
    }

    [Fact]
    public void ParsingIsDeterministicByValue()
    {
        const string source = """
            namespace App;
            public sealed partial class C : Tempest.StatefulComponent
            {
                [Tempest.Reactive] private int _count;
                [Tempest.Command] private void Go() { }
            }
            """;

        Assert.Equal(Parse(source), Parse(source));
    }
}
