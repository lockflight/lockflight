using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The judge's risk routing on real git histories: red/green and trace apply only when production code lines changed (a
/// production file, the C# logic of a Razor file, a test project production ships), and a raise that cannot be read is an error.
/// </summary>
public sealed class JudgeRoutingTests : IAsyncLifetime
{
    private const string TestProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"xunit.v3\" Version=\"3.0.0\" />\n  </ItemGroup>\n</Project>\n";
    private const string BasePrice = "namespace Shop;\n\npublic sealed class Price\n{\n    public int One => 1;\n\n    public int Two => 2;\n}\n";
    private const string Page = "<h1>Prices</h1>\n<p>@price</p>\n\n@code {\n    private decimal price = 2m;\n}\n";

    private GitHistoryFixture repository = null!;
    private string baseCommit = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        baseCommit = await repository.Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n")
            .Write("src/Shop/Price.cs", BasePrice)
            .Write("src/Shop/Prices.razor", Page)
            .Write("tests/Shop.Tests/Shop.Tests.csproj", TestProject)
            .Write("tests/Shop.Tests/PriceTests.cs", "namespace Shop.Tests;\n\npublic sealed class PriceTests\n{\n}\n")
            .CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("012-prices", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ATestOnlyChangeNeedsNoRedGreenOrTrace()
    {
        await repository.Write("tests/Shop.Tests/PriceTests.cs", "namespace Shop.Tests;\n\npublic sealed class PriceTests\n{\n    public int Two => 2;\n}\n")
            .CommitAsync("test: more", TestContext.Current.CancellationToken);

        JudgePlan plan = await PlanAsync();

        Assert.Equal(InterimClassification.RoutingSource, plan.Class.Source);
        Assert.False(plan.Class.ProductionChanged);
        Assert.Equal(CheckLevel.Off, plan.Levels[CheckId.Red]);
        Assert.Equal(CheckLevel.Off, plan.Levels[CheckId.Trace]);
    }

    [Fact]
    public async Task AProductionChangeNeedsRedGreen()
    {
        await repository.Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n    public decimal Shipping() => 2m;\n}\n")
            .CommitAsync("feat: shipping", TestContext.Current.CancellationToken);

        JudgePlan plan = await PlanAsync();

        Assert.True(plan.Class.ProductionChanged);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Red]);
    }

    [Fact]
    public async Task APureRenameOfAProductionFileIsNoProductionChange()
    {
        repository.Delete("src/Shop/Price.cs");
        await repository.Write("src/Shop/Pricing/Price.cs", BasePrice).CommitAsync("refactor: move price", TestContext.Current.CancellationToken);

        JudgePlan plan = await PlanAsync();

        Assert.False(plan.Class.ProductionChanged);
        Assert.Equal(CheckLevel.Off, plan.Levels[CheckId.Red]);
    }

    [Fact]
    public async Task ACommentOnlyEditOfAProductionFileIsNoProductionChange()
    {
        string documented = BasePrice.Replace("    public int One", "    /// <summary>One, as an <see cref=\"int\"/>.</summary>\n    // Kept for callers.\n    public int One", StringComparison.Ordinal);
        await repository.Write("src/Shop/Price.cs", documented).CommitAsync("docs: price", TestContext.Current.CancellationToken);

        Assert.False((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AReindentOfAProductionFileIsNoProductionChange()
    {
        await repository.Write("src/Shop/Price.cs", BasePrice.Replace("    public", "  public", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal))
            .CommitAsync("style: indent", TestContext.Current.CancellationToken);

        Assert.False((await PlanAsync()).Class.ProductionChanged);
    }

    [Theory]
    [InlineData("\"\"\"\"")]
    [InlineData("\"\"\"")]
    public async Task AnEditInsideARawStringLiteralIsAProductionChange(string quotes)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string Sql(string value) => $"namespace Shop;\n\npublic static class Queries\n{{\n    public const string Sql = {quotes}\n        SELECT * FROM t WHERE x = {value}\n        {quotes};\n}}\n";
        await repository.Write("src/Shop/Queries.cs", Sql("1")).CommitAsync("feat: query", cancellationToken);
        baseCommit = await repository.HeadAsync(cancellationToken);
        await repository.Write("src/Shop/Queries.cs", Sql("2")).CommitAsync("fix: query", cancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Theory]
    [InlineData("\"\"\"\"", "    FROM t /* hint */", "    FROM u /* hint */")]
    [InlineData("\"\"\"\"", "    *", "    * 2")]
    [InlineData("\"\"\"", "    // x", "    // y")]
    [InlineData("@\"", "    // x", "    // y")]
    [InlineData("\"\"\"", "    a b", "    a  b")]
    [InlineData("@\"", "    a", "      a")]
    [InlineData("@\"", "    a", "\n    a")]
    public async Task AnEditInsideAMultiLineLiteralThatReadsLikeACommentOrWhitespaceIsAProductionChange(string opening, string before, string after)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string closing = opening == "@\"" ? "\"" : opening;
        string Text(string line) => $"namespace Shop;\n\npublic static class Queries\n{{\n    public const string Sql = {opening}\n{line}\n    {closing};\n}}\n";
        await repository.Write("src/Shop/Queries.cs", Text(before)).CommitAsync("feat: query", cancellationToken);
        baseCommit = await repository.HeadAsync(cancellationToken);
        await repository.Write("src/Shop/Queries.cs", Text(after)).CommitAsync("fix: query", cancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ACommentEditBelowAVerbatimStringHoldingAQuoteIsNoProductionChange()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string Text(string comment) => $"namespace Shop;\n\npublic static class Quotes\n{{\n    public const string One = @\"\"\"\";\n\n    // {comment}\n    public static int Two => 2;\n}}\n";
        await repository.Write("src/Shop/Quotes.cs", Text("one quote")).CommitAsync("feat: quotes", cancellationToken);
        baseCommit = await repository.HeadAsync(cancellationToken);
        await repository.Write("src/Shop/Quotes.cs", Text("a single quote")).CommitAsync("docs: quotes", cancellationToken);

        Assert.False((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AWhitespaceEditInsideAOneLineStringIsAProductionChange()
    {
        await repository.Write("src/Shop/Price.cs", BasePrice.Replace("public int Two => 2;", "public string Two => \"a b\";", StringComparison.Ordinal))
            .CommitAsync("feat: two", TestContext.Current.CancellationToken);
        baseCommit = await repository.HeadAsync(TestContext.Current.CancellationToken);
        await repository.Write("src/Shop/Price.cs", BasePrice.Replace("public int Two => 2;", "public string Two => \"a  b\";", StringComparison.Ordinal))
            .CommitAsync("fix: two", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task CommentingOutACodeLineIsAProductionChange()
    {
        await repository.Write("src/Shop/Price.cs", BasePrice.Replace("    public int Two", "    // public int Two", StringComparison.Ordinal))
            .CommitAsync("refactor: drop two", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AnAutoGeneratedMarkerIsAProductionChange()
    {
        await repository.Write("src/Shop/Price.cs", "// <auto-generated/>\n" + BasePrice).CommitAsync("chore: generated", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Theory]
    [InlineData("src/Shop/.hidden/Price.cs")]
    [InlineData("src/Shop/Bin/Price.cs")]
    public async Task AMoveIntoAFolderTheBuildLeavesOutIsAProductionChange(string path)
    {
        repository.Delete("src/Shop/Price.cs");
        await repository.Write(path, BasePrice).CommitAsync("refactor: move price", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AMovedRazorFileIsAProductionChange()
    {
        repository.Delete("src/Shop/Prices.razor");
        await repository.Write("src/Shop/Pages/Prices.razor", Page).CommitAsync("refactor: move page", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ReorderedProductionLinesAreAProductionChange()
    {
        string reordered = "namespace Shop;\n\npublic sealed class Price\n{\n    public int Two => 2;\n\n    public int One => 1;\n}\n";
        await repository.Write("src/Shop/Price.cs", reordered).CommitAsync("refactor: order", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AMovedFileWithAChangedNamespaceIsAProductionChange()
    {
        repository.Delete("src/Shop/Price.cs");
        await repository.Write("src/Shop/Pricing/Price.cs", BasePrice.Replace("namespace Shop;", "namespace Shop.Pricing;", StringComparison.Ordinal))
            .CommitAsync("refactor: move price", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AChangedPreprocessorLineIsAProductionChange()
    {
        string conditional = BasePrice.Replace("    public int Two => 2;", "#if DEBUG\n    public int Two => 2;\n#endif", StringComparison.Ordinal);
        await repository.Write("src/Shop/Price.cs", conditional).CommitAsync("feat: debug only", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ACommentInARazorCodeBlockStillCountsAsProductionCode()
    {
        await repository.Write("src/Shop/Prices.razor", Page.Replace("2m;", "2m; // the base price", StringComparison.Ordinal))
            .CommitAsync("docs: price", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task SwappingTwoProductionLinesNeedsRedGreen()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Guard = "namespace Shop;\n\npublic sealed class Price\n{\n    public bool Allowed(bool admin)\n    {\n        if (admin)\n            return true;\n        return false;\n    }\n}\n";
        baseCommit = await repository.Write("src/Shop/Price.cs", Guard).CommitAsync("feat: guard", cancellationToken);
        await repository.Write("src/Shop/Price.cs", Guard.Replace("return true;\n        return false;", "return false;\n        return true;", StringComparison.Ordinal))
            .CommitAsync("refactor: swap", cancellationToken);

        JudgePlan plan = await PlanAsync();

        Assert.True(plan.Class.ProductionChanged);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Red]);
    }

    [Fact]
    public async Task SwappingTwoProductionLinesOfTheSameIndentNeedsRedGreen()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Pipeline = "namespace Shop;\n\npublic static class Pipeline\n{\n    public static void Use(App app)\n    {\n        app.UseAuthentication();\n        app.UseAuthorization();\n    }\n}\n";
        baseCommit = await repository.Write("src/Shop/Pipeline.cs", Pipeline).CommitAsync("feat: pipeline", cancellationToken);
        await repository.Write("src/Shop/Pipeline.cs", Pipeline.Replace("app.UseAuthentication();\n        app.UseAuthorization();", "app.UseAuthorization();\n        app.UseAuthentication();", StringComparison.Ordinal))
            .CommitAsync("refactor: swap", cancellationToken);

        JudgePlan plan = await PlanAsync();

        Assert.True(plan.Class.ProductionChanged);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Red]);
    }

    [Fact]
    public async Task CodeCompiledFromAFileThatIsNotCSharpNeedsRedGreen()
    {
        await repository.Write("src/Shop/Pricing.logic", "namespace Shop;\n\npublic static class Pricing\n{\n    public static decimal Shipping(decimal kilograms) => 0m;\n}\n")
            .Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <Compile Include=\"Pricing.logic\" />\n  </ItemGroup>\n</Project>\n")
            .CommitAsync("feat: pricing", TestContext.Current.CancellationToken);

        JudgePlan plan = await PlanAsync();

        Assert.True(plan.Class.ProductionChanged);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Red]);
    }

    [Fact]
    public async Task AnEditToAFileABuildWideGlobCompilesNeedsRedGreen()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        baseCommit = await repository.Write("Directory.Build.props", "<Project>\n  <ItemGroup>\n    <Compile Include=\"**/*.logic\" />\n  </ItemGroup>\n</Project>\n")
            .Write("src/Shop/Pricing.logic", "namespace Shop;\n\npublic static class Pricing\n{\n    public static decimal Shipping(decimal kilograms) => 2m;\n}\n")
            .CommitAsync("build: logic files", cancellationToken);
        await repository.Write("src/Shop/Pricing.logic", "namespace Shop;\n\npublic static class Pricing\n{\n    public static decimal Shipping(decimal kilograms) => 0m;\n}\n")
            .CommitAsync("fix: shipping", cancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AnEditToAFileTheDefaultSourceExtensionCompilesNeedsRedGreen()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        baseCommit = await repository.Write("Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <DefaultLanguageSourceExtension>.logic</DefaultLanguageSourceExtension>\n  </PropertyGroup>\n</Project>\n")
            .Write("src/Shop/Pricing.logic", "namespace Shop;\n\npublic static class Pricing\n{\n    public static decimal Shipping(decimal kilograms) => 2m;\n}\n")
            .CommitAsync("build: logic files", cancellationToken);
        await repository.Write("src/Shop/Pricing.logic", "namespace Shop;\n\npublic static class Pricing\n{\n    public static decimal Shipping(decimal kilograms) => 0m;\n}\n")
            .CommitAsync("fix: shipping", cancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ASingleQuotedCompileIncludeCountsToo()
    {
        await repository.Write("src/Shop/Pricing.logic", "namespace Shop;\n\npublic static class Pricing\n{\n    public static decimal Shipping(decimal kilograms) => 0m;\n}\n")
            .Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <Compile Include='Pricing.logic' />\n  </ItemGroup>\n</Project>\n")
            .CommitAsync("feat: pricing", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"Humanizer\" Version=\"3.0.1\" />\n  </ItemGroup>\n</Project>\n")]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <Compile Update=\"Properties/Resources.Designer.cs\">\n      <DependentUpon>Resources.resx</DependentUpon>\n    </Compile>\n  </ItemGroup>\n</Project>\n")]
    public async Task AProjectEditThatCompilesNothingNewIsNotProductionCode(string project)
    {
        await repository.Write("src/Shop/Shop.csproj", project).CommitAsync("build: project", TestContext.Current.CancellationToken);

        Assert.False((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ACompileItemUnderTestProjectsOnlyIsNotProductionCode()
    {
        await repository.Write("tests/Directory.Build.props", "<Project>\n  <ItemGroup>\n    <Compile Remove=\"**/Fixtures/**\" />\n  </ItemGroup>\n</Project>\n")
            .CommitAsync("build: test fixtures", TestContext.Current.CancellationToken);

        Assert.False((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AConfigFileNoCompileItemNamesIsNotProductionCode()
    {
        await repository.Write("src/Shop/appsettings.json", "{ \"Shipping\": 2 }\n").CommitAsync("config: shipping", TestContext.Current.CancellationToken);

        Assert.False((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task MarkupOutsideTheCodeBlockIsNotProductionCode()
    {
        await repository.Write("src/Shop/Prices.razor", Page.Replace("<h1>Prices</h1>", "<h1 class=\"title\">Prices</h1>", StringComparison.Ordinal))
            .CommitAsync("style: title", TestContext.Current.CancellationToken);

        Assert.False((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task TheCodeBlockOfARazorFileIsProductionCode()
    {
        await repository.Write("src/Shop/Prices.razor", Page.Replace("2m", "3m", StringComparison.Ordinal))
            .CommitAsync("fix: price", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ANewTestProjectThatProductionReferencesShips()
    {
        await repository.Write("src/Helpers.Tests/Helpers.Tests.csproj", TestProject)
            .Write("src/Helpers.Tests/Rounding.cs", "namespace Helpers;\n\npublic static class Rounding\n{\n    public static decimal Up(decimal value) => decimal.Ceiling(value);\n}\n")
            .Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <ProjectReference Include=\"../Helpers.Tests/Helpers.Tests.csproj\" />\n  </ItemGroup>\n</Project>\n")
            .CommitAsync("feat: rounding", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ACSharpFileMarkedBinaryIsStillProductionCode()
    {
        await repository.Write(".gitattributes", "*.cs -diff\n")
            .Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n    public decimal Shipping() => 2m;\n}\n")
            .CommitAsync("feat: shipping", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task AProductionProjectTurnedIntoATestProjectStaysProductionCode()
    {
        await repository.Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <IsTestProject>true</IsTestProject>\n  </PropertyGroup>\n</Project>\n")
            .Write("src/Shop/Discount.cs", "namespace Shop;\n\npublic static class Discount\n{\n    public static decimal Off(decimal value) => value / 10m;\n}\n")
            .CommitAsync("feat: discount", TestContext.Current.CancellationToken);

        Assert.True((await PlanAsync()).Class.ProductionChanged);
    }

    [Fact]
    public async Task ARaiseThatCannotBeReadIsAnErrorNotALowerClass()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n    public int Two => 2;\n}\n").CommitAsync("feat: two", cancellationToken);
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, baseCommit, head, cancellationToken);
        RoutingPolicy routing = RoutingPolicy.From(null, new PolicySource(baseCommit, baseCommit, null, "standard"));

        JudgeRouting routed = JudgePlanner.Route(reading.ChangeSet!, routing, new RaiseReading(RaiseReadStatus.GitFailed, [], ["git could not read the covering features' specs or records"]));

        Assert.Null(routed.Class);
        Assert.Contains("raises could not be read", routed.Error, StringComparison.Ordinal);
    }

    private async Task<JudgePlan> PlanAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.HeadAsync(cancellationToken);
        JudgeInputs inputs = new(repository.Root, head, baseCommit, "012-prices", JudgePlatform.GitHub, JudgeEvent.PullRequest, Path.Combine(repository.Root, "out"), null, ReviewExpected: true);
        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, EnvironmentReader.Of(new Dictionary<string, string>()), cancellationToken);
        Assert.True(planning.Plan is not null, planning.Error);
        return planning.Plan;
    }
}
