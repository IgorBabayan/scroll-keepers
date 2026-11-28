using System.Text;
using ScrollKeeper.Presentation.Documents;

namespace ScrollKeeper.Tests.Documents;

public sealed class BbCodeParserTests
{
    /// <summary>Compact text form of a tree: <c>text</c>, <c>[name=arg](children)</c>.</summary>
    private static string Dump(BbNode node)
    {
        var builder = new StringBuilder();
        Write(node, builder);
        return builder.ToString();

        static void Write(BbNode node, StringBuilder builder)
        {
            switch (node)
            {
                case BbText text:
                    builder.Append('"').Append(text.Text).Append('"');
                    break;
                case BbTag tag:
                    builder.Append('[').Append(tag.Name);
                    if (tag.Argument is not null)
                        builder.Append('=').Append(tag.Argument);
                    builder.Append("](");
                    for (var i = 0; i < tag.Children.Count; i++)
                    {
                        if (i > 0)
                            builder.Append(',');
                        Write(tag.Children[i], builder);
                    }

                    builder.Append(')');
                    break;
            }
        }
    }

    private static string Parse(string? input) => Dump(BbCodeParser.Parse(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_EmptyInput_ReturnsEmptyRoot(string? input)
    {
        var root = BbCodeParser.Parse(input);

        Assert.Equal(BbCodeParser.RootName, root.Name);
        Assert.Empty(root.Children);
    }

    [Theory]
    [InlineData("plain text", "[root](\"plain text\")")]
    [InlineData("[b]bold[/b] text", "[root]([b](\"bold\"),\" text\")")]
    [InlineData("[B]x[/B]", "[root]([b](\"x\"))")]
    [InlineData("[i]unclosed", "[root]([i](\"unclosed\"))")]
    [InlineData("x[/b]", "[root](\"x[/b]\")")]
    [InlineData("a [foo]b[/foo] c", "[root](\"a [foo]b[/foo] c\")")]
    [InlineData("[b][i]x[/b]y", "[root]([b]([i](\"x\")),\"y\")")]
    [InlineData("[h1]Title[/h1]", "[root]([h1](\"Title\"))")]
    public void Parse_BuildsTree(string input, string expected)
    {
        Assert.Equal(expected, Parse(input));
    }

    [Theory]
    [InlineData("[url=\"https://esoui.com\"]link[/url]", "[root]([url=https://esoui.com](\"link\"))")]
    [InlineData("[color='red']x[/color]", "[root]([color=red](\"x\"))")]
    [InlineData("[size= 3 ]x[/size]", "[root]([size=3](\"x\"))")]
    public void Parse_UnquotesArguments(string input, string expected)
    {
        Assert.Equal(expected, Parse(input));
    }

    [Theory]
    [InlineData("[code][b]x[/b][/code]after", "[root]([code](\"[b]x[/b]\"),\"after\")")]
    [InlineData("[CODE]raw[/code]", "[root]([code](\"raw\"))")]
    [InlineData("[noparse]abc", "[root]([noparse](\"abc\"))")]
    [InlineData("[code][/code]", "[root]([code]())")]
    public void Parse_DoesNotParseRawTagContent(string input, string expected)
    {
        Assert.Equal(expected, Parse(input));
    }

    [Theory]
    [InlineData("[list][*]one[*]two[/list]", "[root]([list]([*](\"one\"),[*](\"two\")))")]
    [InlineData("[list][*][b]one[*]two[/list]", "[root]([list]([*]([b](\"one\")),[*](\"two\")))")]
    [InlineData("[list=1][*]a[/*][*]b[/list]", "[root]([list=1]([*](\"a\"),[*](\"b\")))")]
    [InlineData("[*]x", "[root](\"• x\")")]
    [InlineData("a[/*]b", "[root](\"ab\")")]
    public void Parse_HandlesListItems(string input, string expected)
    {
        Assert.Equal(expected, Parse(input));
    }

    [Fact]
    public void Parse_NormalizesLineEndings()
    {
        Assert.Equal("[root](\"a\nb\nc\")", Parse("a\r\nb\rc"));
    }

    [Fact]
    public void Parse_LargeInput_StaysLinear()
    {
        var input = string.Concat(Enumerable.Repeat("[b]x[/b] [unknown] ", 20_000));

        var root = BbCodeParser.Parse(input);

        Assert.Equal(40_000, root.Children.Count);
    }
}
