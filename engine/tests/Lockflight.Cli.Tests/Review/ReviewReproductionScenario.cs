using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Review;

/// <summary>Reviews of a small .NET repository with two test projects, building and running real reproductions.</summary>
public abstract class ReviewReproductionScenario : IDisposable
{
    private const string Feature = "001-pricing";
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>";
    private protected const string Broken = """{"severity":"major","file":"src/Shop/PriceCalculator.cs","line":5,"title":"Discount ignored","evidence":"Total returns the price","reproduction":"[Fact]\npublic void TotalAppliesTheDiscount() => Assert.Equal(90m, PriceCalculator.Totl(100m, 10m));"}""";
    private protected const string Repaired = """{"severity":"major","file":"src/Shop/PriceCalculator.cs","line":5,"title":"Discount ignored","evidence":"Total returns the price","reproduction":"[Fact]\npublic void TotalAppliesTheDiscount() => Assert.Equal(90m, PriceCalculator.Total(100m, 10m));"}""";

    private protected const string MustSpec = "- **FR-001**: The total MUST apply the discount.\n";

    private protected const string SubtractTheDiscount =
        "--- a/src/Shop/PriceCalculator.cs\n+++ b/src/Shop/PriceCalculator.cs\n@@ -5 +5 @@\n-    public static decimal Total(decimal price, decimal discount) => price;\n+    public static decimal Total(decimal price, decimal discount) => price - discount;\n";

    private protected readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-repro-");
    private readonly DirectoryInfo tools = Directory.CreateTempSubdirectory("lockflight-repro-path-");
    private protected readonly ManualTime time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private protected readonly FakeClaude claude = new();

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(tools);
        GC.SuppressFinalize(this);
    }

    private protected async Task RepositoryAsync(string spec = "- **FR-001**: The total applies the discount.\n")
    {
        await GitAsync("init", "-q", "-b", "main");
        await GitAsync("config", "user.email", "reviewer@example.com");
        await GitAsync("config", "user.name", "Reviewing Person");
        await GitAsync("config", "commit.gpgsign", "false");
        Write(Manifest.FileName, "version: 2\n");
        Write(".gitignore", "bin/\nobj/\n");
        Write($"specs/{Feature}/spec.md", spec);
        Write("Shop.slnx", "<Solution>\n  <Project Path=\"src/Billing/Billing.csproj\" />\n  <Project Path=\"src/Shop/Shop.csproj\" />\n  <Project Path=\"tests/Billing.Tests/Billing.Tests.csproj\" />\n  <Project Path=\"tests/Shop.Tests/Shop.Tests.csproj\" />\n</Solution>\n");
        Write("src/Billing/Billing.csproj", Library);
        Write("src/Billing/Invoice.cs", "namespace Billing;\n\npublic sealed record Invoice(decimal Amount);\n");
        Write("src/Shop/Shop.csproj", Library);
        Write("src/Shop/PriceCalculator.cs", "namespace Shop.Pricing;\n\npublic static class PriceCalculator\n{\n    public static decimal Total(decimal price, decimal discount) => price - discount;\n}\n");
        TestProject("Billing", "Billing.Tests", "using Billing;", "Assert.Equal(5m, new Invoice(5m).Amount);");
        TestProject("Shop", "Shop.Tests", "using Shop.Pricing;", "Assert.Equal(10m, PriceCalculator.Total(10m, 0m));");
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "start");
        Write("src/Shop/PriceCalculator.cs", "namespace Shop.Pricing;\n\npublic static class PriceCalculator\n{\n    public static decimal Total(decimal price, decimal discount) => price;\n}\n");
        File.WriteAllText(Path.Combine(tools.FullName, OperatingSystem.IsWindows() ? "claude.exe" : "claude"), string.Empty);
    }

    private void TestProject(string library, string name, string usingDirective, string assertion)
    {
        Write($"tests/{name}/{name}.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" />
                <ProjectReference Include="../../src/{library}/{library}.csproj" />
                <Using Include="Xunit" />
              </ItemGroup>
            </Project>
            """);
        Write($"tests/{name}/SmokeTests.cs", $"{usingDirective}\n\nnamespace {name};\n\npublic sealed class SmokeTests\n{{\n    [Fact]\n    public void Works() => {assertion}\n}}\n");
    }

    private void Write(string path, string content)
    {
        string full = Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private async Task GitAsync(params string[] arguments) =>
        await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);

    private protected static string MinorMust(string assertion = "Assert.Equal(90m, PriceCalculator.Total(100m, 10m))") =>
        $$"""{"severity":"minor","file":"src/Shop/PriceCalculator.cs","line":5,"title":"Discount ignored","evidence":"Total returns the price","specQuote":"The total MUST apply the discount.","reproduction":"[Fact]\npublic void TotalAppliesTheDiscount() => {{assertion}};"}""";

    private protected static string WithFix(string finding, string fix) => finding[..^1] + $",\"fix\":{JsonSerializer.Serialize(fix)}}}";

    private protected static JsonElement SingleFinding(string runFile)
    {
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(runFile));
        return Assert.Single(evidence.RootElement.GetProperty("findings").EnumerateArray().ToArray()).Clone();
    }

    private protected string ReviewsDirectory() => Path.Combine(root.FullName, ".lockflight", "features", Feature, "reviews");

    private protected async Task<(ExitCode ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, time)
        {
            Environment = EnvironmentReader.Of(new Dictionary<string, string> { [EnvironmentVariables.Path] = tools.FullName }),
            Claude = claude.RunAsync,
        };
        ExitCode exitCode = await CliApplication.RunAsync(arguments.Length == 0 ? ["review"] : arguments, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString() + error.ToString());
    }
}
