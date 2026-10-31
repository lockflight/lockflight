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
    private const string Page = "<h1>Prices</h1>\n<p>@price</p>\n\n@code {\n    private decimal price = 2m;\n}\n";

    private GitHistoryFixture repository = null!;
    private string baseCommit = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        baseCommit = await repository.Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n")
            .Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n}\n")
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
