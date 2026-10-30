using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class RazorCodeBlocksTests
{
    [Fact]
    public void CodeAndFunctionsBlocksAndMultiLineBlocksHoldLogicButMarkupDoesNot()
    {
        const string Text = "<h1>@title</h1>\n@{ var single = 1; }\n@{\n    var many = \"}\";\n}\n<p>don't</p>\n@functions {\n    int Two() => 2; // }\n}\n@code{\n    int three = 3;\n}\n";

        Assert.Equal([3, 4, 5, 7, 8, 9, 10, 11, 12], RazorCodeBlocks.Lines(Text).Order());
    }

    [Fact]
    public void AnUnclosedBlockRunsToTheEnd() => Assert.Equal([2, 3], RazorCodeBlocks.Lines("<p>x</p>\n@code {\n    int x;\n").Order());
}
