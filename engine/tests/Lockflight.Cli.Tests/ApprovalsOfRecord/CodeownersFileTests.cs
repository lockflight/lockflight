using Lockflight.Cli.ApprovalsOfRecord;

namespace Lockflight.Cli.Tests.ApprovalsOfRecord;

public sealed class CodeownersFileTests
{
    [Theory]
    [InlineData("/specs/", "specs/012-checkout/spec.md", true)]
    [InlineData("/specs/", "specs", false)]
    [InlineData("/specs/", "src/specs/a.md", false)]
    [InlineData("specs/", "src/specs/a.md", true)]
    [InlineData("specs", "src/specs/a.md", true)]
    [InlineData("specs/**", "specs/012/spec.md", true)]
    [InlineData("specs/**", "src/specs/012/spec.md", false)]
    [InlineData("specs/*", "specs/012/spec.md", false)]
    [InlineData("/specs/*", "specs/spec.md", true)]
    [InlineData("/specs/*.md", "specs/012/spec.md", false)]
    [InlineData("specs/**/*.md", "specs/012/spec.md", true)]
    [InlineData("/apps/", "apps/x/y.cs", true)]
    [InlineData("specs/*.md", "specs/012/spec.md", false)]
    [InlineData("*.md", "specs/012/spec.md", true)]
    [InlineData("**/spec.md", "specs/012/spec.md", true)]
    [InlineData("*", "specs/012/spec.md", true)]
    [InlineData("/docs/**/spec?.md", "docs/a/b/spec1.md", true)]
    [InlineData("/docs/**/spec?.md", "docs/a/b/spec10.md", false)]
    [InlineData("a.b", "aXb", false)]
    public void PatternsMatchAsThePlatformsReadThem(string pattern, string path, bool expected) =>
        Assert.Equal(expected, CodeownersPattern.Matches(pattern, path));

    [Fact]
    public void TheLastMatchingRuleDecidesAndARuleWithoutOwnersLeavesNone()
    {
        CodeownersFile codeowners = CodeownersFile.Parse(".github/CODEOWNERS", """
            # Specs need product owners
            *                 @org/everyone
            /specs/           @org/product   # inline comment
            /specs/drafts/
            /a\#b             @org/hash
            [Product] @org/other
            """, sections: false);

        Assert.Equal(["@org/product"], codeowners.OwnersOf("specs/012/spec.md"));
        Assert.Empty(codeowners.OwnersOf("specs/drafts/x.md"));
        Assert.Equal(["@org/everyone"], codeowners.OwnersOf("src/a.cs"));
        Assert.Equal(["@org/hash"], codeowners.OwnersOf("a#b"));
        Assert.Equal(4, codeowners.Rules.Count);
        Assert.Equal(["@org/product"], codeowners.OwnersOf("specs/x.md"));
    }

    [Fact]
    public void GitLabSectionsEachDecideAndLendTheirDefaultOwners()
    {
        CodeownersFile codeowners = CodeownersFile.Parse("CODEOWNERS", """
            [Product] @org/product
            /specs/
            [Security][2] @org/security
            /specs/payments/
            ^[Optional]
            /specs/drafts/
            """, sections: true);

        Assert.Equal(["@org/product", "@org/security"], codeowners.OwnersOf("specs/payments/spec.md"));
        Assert.Equal(["@org/product"], codeowners.OwnersOf("specs/012/spec.md"));
        Assert.Equal(["@org/product"], codeowners.OwnersOf("specs/drafts/a.md"));
        Assert.Empty(CodeownersFile.Parse("CODEOWNERS", "^[Docs] @org/docs\n/specs/\n", sections: true).OwnersOf("specs/a.md"));
        Assert.Empty(codeowners.OwnersOf("src/a.cs"));
    }

    [Fact]
    public void OnlyRealOwnersCountAndInvalidLinesAreSkipped()
    {
        CodeownersFile codeowners = CodeownersFile.Parse(".github/CODEOWNERS", """
            /specs/  @org/product
            /specs/a/ TODO product
            !/specs/b/ @org/none
            /specs/[cd]/
            /specs/e/ dev@example.com
            """, sections: false);

        Assert.Empty(codeowners.OwnersOf("specs/a/spec.md"));
        Assert.Equal(["@org/product"], codeowners.OwnersOf("specs/b/spec.md"));
        Assert.Equal(["@org/product"], codeowners.OwnersOf("specs/c/spec.md"));
        Assert.Equal(["dev@example.com"], codeowners.OwnersOf("specs/e/spec.md"));
    }
}
