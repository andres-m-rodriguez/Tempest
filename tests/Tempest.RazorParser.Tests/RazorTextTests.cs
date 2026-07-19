using Xunit;

namespace Tempest.RazorParser.Tests;

public class RazorTextTests
{
    private static List<(int Start, string Text)> Blocks(string text)
        => RazorText.ExtractCodeBlocks(text).ToList();

    [Fact]
    public void FindsSimpleBlock()
    {
        var text = "<h1>Hi</h1>\n@code {\n    int x;\n}\n";
        var blocks = Blocks(text);

        var block = Assert.Single(blocks);
        Assert.Equal("\n    int x;\n", block.Text);
        Assert.Equal(text.IndexOf('{') + 1, block.Start);
    }

    [Fact]
    public void ContentOffsetRoundTrips()
    {
        var text = "@code { void M() { } }";
        var block = Assert.Single(Blocks(text));
        Assert.Equal(block.Text, text.Substring(block.Start, block.Text.Length));
    }

    [Fact]
    public void NestedBracesStayInsideBlock()
    {
        var text = "@code { void M() { if (true) { } } }tail";
        var block = Assert.Single(Blocks(text));
        Assert.Equal(" void M() { if (true) { } } ", block.Text);
    }

    [Fact]
    public void BraceInsideStringIsIgnored()
    {
        var text = """@code { var s = "}"; int y; }""";
        var block = Assert.Single(Blocks(text));
        Assert.Contains("int y;", block.Text);
    }

    [Fact]
    public void EscapedQuoteInsideStringIsIgnored()
    {
        var text = """@code { var s = "\"}"; int k; }""";
        var block = Assert.Single(Blocks(text));
        Assert.Contains("int k;", block.Text);
    }

    [Fact]
    public void BraceInsideVerbatimStringIsIgnored()
    {
        var text = """@code { var s = @"} "" }"; int v; }""";
        var block = Assert.Single(Blocks(text));
        Assert.Contains("int v;", block.Text);
    }

    [Fact]
    public void BraceInsideInterpolatedVerbatimStringsIsIgnored()
    {
        var text = """@code { var a = $@"} x"; var b = @$"}"; int z; }""";
        var block = Assert.Single(Blocks(text));
        Assert.Contains("int z;", block.Text);
    }

    [Fact]
    public void BraceInsideCharLiteralIsIgnored()
    {
        var text = @"@code { var c = '}'; var q = '\''; int w; }";
        var block = Assert.Single(Blocks(text));
        Assert.Contains("int w;", block.Text);
    }

    [Fact]
    public void BraceInsideLineCommentIsIgnored()
    {
        var text = "@code { // } not the end\n int x; }";
        var block = Assert.Single(Blocks(text));
        Assert.Contains("int x;", block.Text);
    }

    [Fact]
    public void BraceInsideBlockCommentIsIgnored()
    {
        var text = "@code { /* } */ int x; }";
        var block = Assert.Single(Blocks(text));
        Assert.Contains("int x;", block.Text);
    }

    [Fact]
    public void WhitespaceBetweenCodeAndBraceIsAllowed()
    {
        var text = "@code   \n  { int x; }";
        Assert.Single(Blocks(text));
    }

    [Fact]
    public void WordEndingInCodeIsNotABlock()
    {
        Assert.Empty(Blocks("user@code { int x; }"));
        Assert.Empty(Blocks("mycode { int x; }"));
    }

    [Fact]
    public void EscapedAtIsNotABlock()
    {
        Assert.Empty(Blocks("@@code { int x; }"));
    }

    [Fact]
    public void CodeWithoutBraceIsNotABlock()
    {
        Assert.Empty(Blocks("@code is a razor keyword"));
    }

    [Fact]
    public void UnterminatedBlockYieldsNothing()
    {
        Assert.Empty(Blocks("@code { int x;"));
        Assert.Empty(Blocks("@code { var s = \"unterminated }"));
        Assert.Empty(Blocks("@code { /* unterminated }"));
    }

    [Fact]
    public void FindsMultipleBlocks()
    {
        var text = "@code { int a; }\n<p>mid</p>\n@code { int b; }";
        var blocks = Blocks(text);

        Assert.Equal(2, blocks.Count);
        Assert.Contains("int a;", blocks[0].Text);
        Assert.Contains("int b;", blocks[1].Text);
    }

    [Fact]
    public void FindMatchingBraceSkipsAllLiteralKinds()
    {
        var text = "{ \"}\" + '}' /* } */ // }\n}";
        Assert.Equal(text.Length - 1, RazorText.FindMatchingBrace(text, 0));
    }

    [Fact]
    public void SkipStringHonorsEscapes()
    {
        var ordinary = """x"a\"b"y""";
        Assert.Equal(ordinary.IndexOf("\"y", StringComparison.Ordinal), RazorText.SkipString(ordinary, 1, verbatim: false));

        var verbatim = """x"a""b"y""";
        Assert.Equal(verbatim.IndexOf("\"y", StringComparison.Ordinal), RazorText.SkipString(verbatim, 1, verbatim: true));

        Assert.Equal(-1, RazorText.SkipString("\"never closes", 0, verbatim: false));
    }

    [Fact]
    public void ExtractsUsingDirectives()
    {
        var text = "@using System.Text\n  @using My.Lib;\n<p>not @using Inline</p>\n";
        Assert.Equal("System.Text\nMy.Lib", RazorText.ExtractUsingDirectives(text));
    }

    [Fact]
    public void NoUsingsYieldsEmpty()
    {
        Assert.Equal("", RazorText.ExtractUsingDirectives("<h1>plain markup</h1>"));
    }
}
