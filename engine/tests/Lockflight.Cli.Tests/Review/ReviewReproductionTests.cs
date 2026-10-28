using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Review;

/// <summary>Reviews of a small .NET repository with two test projects, building and running real reproductions.</summary>
public sealed class ReviewReproductionTests : IDisposable
{
    private const string Feature = "001-pricing";
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>";
    private const string Broken = """{"severity":"major","file":"src/Shop/PriceCalculator.cs","line":5,"title":"Discount ignored","evidence":"Total returns the price","reproduction":"[Fact]\npublic void TotalAppliesTheDiscount() => Assert.Equal(90m, PriceCalculator.Totl(100m, 10m));"}""";
    private const string Repaired = """{"severity":"major","file":"src/Shop/PriceCalculator.cs","line":5,"title":"Discount ignored","evidence":"Total returns the price","reproduction":"[Fact]\npublic void TotalAppliesTheDiscount() => Assert.Equal(90m, PriceCalculator.Total(100m, 10m));"}""";

    private const string MustSpec = "- **FR-001**: The total MUST apply the discount.\n";

    private const string SubtractTheDiscount =
        "--- a/src/Shop/PriceCalculator.cs\n+++ b/src/Shop/PriceCalculator.cs\n@@ -5 +5 @@\n-    public static decimal Total(decimal price, decimal discount) => price;\n+    public static decimal Total(decimal price, decimal discount) => price - discount;\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-repro-");
    private readonly DirectoryInfo tools = Directory.CreateTempSubdirectory("lockflight-repro-path-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeClaude claude = new();

    [Fact]
    public async Task AReproductionThatDoesNotCompileIsRepairedOnceAndThenConfirmsTheFinding()
    {
        await RepositoryAsync();
        claude.Answers(Broken).Answers(Repaired);

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(ReviewsDirectory(), "*.json"))));
        JsonElement finding = Assert.Single(evidence.RootElement.GetProperty("findings").EnumerateArray().ToArray());
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("failed", finding.GetProperty("reproductionResult").GetString());
        Assert.Equal(2, finding.GetProperty("reproductionAttempts").GetInt32());
        Assert.Equal("none", finding.GetProperty("fixCheck").GetString());
        Assert.False(finding.GetProperty("fixChecked").GetBoolean());
        Assert.Contains("PriceCalculator.Total(100m, 10m)", finding.GetProperty("reproduction").GetString(), StringComparison.Ordinal);
        string errors = Assert.Single(finding.GetProperty("compileErrors").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Contains("Finding1.cs(2,", errors, StringComparison.Ordinal);
        Assert.Contains("Totl", errors, StringComparison.Ordinal);
        Assert.Equal(1, evidence.RootElement.GetProperty("reproductionRepairs").GetProperty("calls").GetInt32());
        Assert.Equal(0.50m, evidence.RootElement.GetProperty("estimatedCostUsd").GetDecimal());

        IReadOnlyList<string>[] reviews = [.. claude.Calls.Where(arguments => arguments.Contains("-p"))];
        Assert.Equal(2, reviews.Length);
        Assert.Equal("2.75", reviews[1][reviews[1].ToList().IndexOf("--max-budget-usd") + 1]);
        Assert.Contains("- tests/Shop.Tests/Shop.Tests.csproj: net10.0, xUnit v3; references src/Shop/Shop.csproj", claude.Prompts[0], StringComparison.Ordinal);
        Assert.Contains("// src/Shop/PriceCalculator.cs\nnamespace Shop.Pricing;", claude.Prompts[0].ReplaceLineEndings("\n"), StringComparison.Ordinal);
        string repair = claude.Prompts[1];
        Assert.Contains("`tests/Shop.Tests/Shop.Tests.csproj`", repair, StringComparison.Ordinal);
        Assert.Contains("public static decimal Total(decimal price, decimal discount) => …;", repair, StringComparison.Ordinal);
        Assert.Contains("Totl", repair, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARepairedReproductionThatPassesLeavesTheFindingUnverified()
    {
        await RepositoryAsync();
        claude.Answers(Broken).Answers(Repaired.Replace("Assert.Equal(90m", "Assert.Equal(100m", StringComparison.Ordinal));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.NotEarned, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("unverified", finding.GetProperty("outcome").GetString());
        Assert.Equal("repaired-passed", finding.GetProperty("reproductionResult").GetString());
        Assert.Contains("Totl", finding.GetProperty("reproduction").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFindingStillOpenFromAnEarlierRunIsNotRepairedAgain()
    {
        await RepositoryAsync();
        claude.Answers(Broken).Answers(Broken).Answers(Broken);

        await RunAsync();
        time.Advance(TimeSpan.FromMinutes(5));
        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.NotEarned, output);
        Assert.Equal(3, claude.Calls.Count(arguments => arguments.Contains("-p")));
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Order(StringComparer.Ordinal).Last());
        Assert.Equal("did-not-compile", finding.GetProperty("reproductionResult").GetString());
        Assert.Equal(1, finding.GetProperty("reproductionAttempts").GetInt32());
    }

    [Fact]
    public async Task OneBrokenReproductionDoesNotKeepTheOthersFromRunning()
    {
        await RepositoryAsync();
        string other = Repaired.Replace("\"line\":5", "\"line\":4", StringComparison.Ordinal).Replace("Discount ignored", "Total ignores the discount", StringComparison.Ordinal);
        claude.Answers(Broken, other).Answers(Broken);

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(ReviewsDirectory(), "*.json").Single()));
        Assert.Equal(
            ["did-not-compile", "failed"],
            evidence.RootElement.GetProperty("findings").EnumerateArray().Select(finding => finding.GetProperty("reproductionResult").GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheEvidenceNamesTheTestProjectEachReproductionWasBuiltIn()
    {
        await RepositoryAsync();
        string minor = Repaired.Replace("\"major\"", "\"minor\"", StringComparison.Ordinal).Replace("Discount ignored", "Naming", StringComparison.Ordinal);
        claude.Answers(Repaired, minor);

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(ReviewsDirectory(), "*.json").Single()));
        Dictionary<string, JsonElement> findings = evidence.RootElement.GetProperty("findings").EnumerateArray()
            .ToDictionary(finding => finding.GetProperty("severity").GetString()!, finding => finding.GetProperty("reproductionProject"));
        Assert.Equal("tests/Shop.Tests/Shop.Tests.csproj", findings["major"].GetString());
        Assert.Equal(JsonValueKind.Null, findings["minor"].ValueKind);
    }

    [Fact]
    public async Task ReproductionsLeaveTheDevelopersFilesAndBuildOutputAlone()
    {
        await RepositoryAsync();
        claude.Answers(Repaired);

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        Assert.DoesNotContain(
            Directory.GetFiles(root.FullName, "*.g.cs", SearchOption.AllDirectories),
            file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        string developerBuild = Path.Combine(root.FullName, "tests", "Shop.Tests", "bin", "Debug", "net10.0", "Shop.Tests.dll");
        Assert.DoesNotContain("LockflightReproductions", Encoding.Latin1.GetString(File.ReadAllBytes(developerBuild)), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root.FullName, "tests", "Shop.Tests", "bin", "lockflight-review", "net10.0", "Shop.Tests.dll")));
    }

    [Fact]
    public async Task AConfirmedFindingWhoseFixHoldsStillFailsAndIsFixChecked()
    {
        await RepositoryAsync();
        claude.Answers(WithFix(Repaired, SubtractTheDiscount));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("held", finding.GetProperty("fixCheck").GetString());
        Assert.True(finding.GetProperty("fixChecked").GetBoolean());
        Assert.Contains("fix-checked", output, StringComparison.Ordinal);
        Assert.Contains("=> price;", File.ReadAllText(Path.Combine(root.FullName, "src", "Shop", "PriceCalculator.cs")), StringComparison.Ordinal);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(ReviewsDirectory(), "*.json").Single()));
        Assert.Equal(1, evidence.RootElement.GetProperty("fixChecks").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task AFixThatStillDoesNotApplyAfterItsRepairLeavesTheFindingConfirmedButNotFixChecked()
    {
        await RepositoryAsync();
        string stale = SubtractTheDiscount.Replace("discount) => price;", "discount) => price * 1;", StringComparison.Ordinal);
        claude.Answers(WithFix(Repaired, stale)).Answers(WithFix(Repaired, stale));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("did-not-apply", finding.GetProperty("fixCheck").GetString());
        Assert.False(finding.GetProperty("fixChecked").GetBoolean());
        Assert.Contains("not fix-checked: the reviewer's fix did not apply", output, StringComparison.Ordinal);
        Assert.Equal(2, finding.GetProperty("fixAttempts").GetInt32());
        Assert.Contains("src/Shop/PriceCalculator.cs", finding.GetProperty("fixCheckErrors").GetString(), StringComparison.Ordinal);
        Assert.Equal(2, claude.Calls.Count(arguments => arguments.Contains("-p")));
        Assert.Contains("did not apply", claude.Prompts[1], StringComparison.Ordinal);
        Assert.Contains("price * 1", claude.Prompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFixRepairedToApplyIsCheckedAgain()
    {
        await RepositoryAsync();
        string stale = SubtractTheDiscount.Replace("discount) => price;", "discount) => price * 1;", StringComparison.Ordinal);
        claude.Answers(WithFix(Repaired, stale)).Answers(WithFix(Repaired, SubtractTheDiscount));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("held", finding.GetProperty("fixCheck").GetString());
        Assert.Equal(2, finding.GetProperty("fixAttempts").GetInt32());
        Assert.Contains("price - discount", finding.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingThatBreaksAnAnchoredMustAndReproducesIsEscalatedAndFixChecked()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(WithFix(MinorMust(), SubtractTheDiscount));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("major", finding.GetProperty("severity").GetString());
        Assert.Equal("minor", finding.GetProperty("originalSeverity").GetString());
        Assert.Equal("escalated: anchored MUST, reproduced", finding.GetProperty("escalation").GetString());
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("open", finding.GetProperty("state").GetString());
        Assert.Equal("anchored", finding.GetProperty("specAnchor").GetString());
        Assert.Equal("held", finding.GetProperty("fixCheck").GetString());
        Assert.Contains("escalated: anchored MUST, reproduced (the reviewer rated it minor)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingWhoseReproductionPassesStaysMinor()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust("Assert.Equal(100m, PriceCalculator.Total(100m, 10m))"));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("originalSeverity").ValueKind);
        Assert.Equal("reported", finding.GetProperty("outcome").GetString());
        Assert.Equal("passed", finding.GetProperty("reproductionResult").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("state").ValueKind);
        Assert.Contains("the reproduction passes on the current code", finding.GetProperty("escalation").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingWhoseReproductionCannotBeRepairedWithinTheBudgetStaysMinorAndSaysSo()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust("Assert.Equal(90m, PriceCalculator.Totl(100m, 10m))"));

        (ExitCode exitCode, string output) = await RunAsync("review", "--budget", "0.25");

        Assert.True(exitCode != ExitCode.Fail, output);
        Assert.Single(claude.Calls, arguments => arguments.Contains("-p"));
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal("did-not-compile", finding.GetProperty("reproductionResult").GetString());
        Assert.Contains("the run's budget was spent before it could be repaired", finding.GetProperty("escalation").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingWhoseQuoteIsNotInTheSpecIsNotEscalatedOrRun()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust().Replace("MUST apply the discount", "MUST round the discount", StringComparison.Ordinal));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal("not-anchored", finding.GetProperty("specAnchor").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("escalation").ValueKind);
        Assert.Equal("none", finding.GetProperty("reproductionResult").GetString());
        Assert.False(Directory.Exists(Path.Combine(root.FullName, "tests", "Shop.Tests", "bin", "lockflight-review")));
    }

    [Fact]
    public async Task AMinorFindingWhoseAnchoredQuoteHasNoMustIsNotEscalated()
    {
        await RepositoryAsync("- **FR-001**: The total applies the discount.\n");
        claude.Answers(MinorMust().Replace("The total MUST apply the discount.", "The total applies the discount.", StringComparison.Ordinal));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal("anchored", finding.GetProperty("specAnchor").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("escalation").ValueKind);
        Assert.Equal("none", finding.GetProperty("reproductionResult").GetString());
    }

    [Fact]
    public async Task AnEscalatedFindingCanBeDismissedAndStaysDismissedWhenRaisedAgain()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust()).Answers(MinorMust());
        await RunAsync();
        string id = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single()).GetProperty("id").GetString()!;

        (ExitCode dismissed, string dismissOutput) = await RunAsync("review", "dismiss", id, "--reason", "The discount is applied upstream");
        time.Advance(TimeSpan.FromMinutes(5));
        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(dismissed == ExitCode.Pass, dismissOutput);
        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Order(StringComparer.Ordinal).Last());
        Assert.Equal("dismissed", finding.GetProperty("state").GetString());
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(tools);
    }

    private async Task RepositoryAsync(string spec = "- **FR-001**: The total applies the discount.\n")
    {
        await GitAsync("init", "-q", "-b", "main");
        await GitAsync("config", "user.email", "reviewer@example.com");
        await GitAsync("config", "user.name", "Reviewing Person");
        await GitAsync("config", "commit.gpgsign", "false");
        Write(Manifest.FileName, "version: 1\n");
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

    private static string MinorMust(string assertion = "Assert.Equal(90m, PriceCalculator.Total(100m, 10m))") =>
        $$"""{"severity":"minor","file":"src/Shop/PriceCalculator.cs","line":5,"title":"Discount ignored","evidence":"Total returns the price","specQuote":"The total MUST apply the discount.","reproduction":"[Fact]\npublic void TotalAppliesTheDiscount() => {{assertion}};"}""";

    private static string WithFix(string finding, string fix) => finding[..^1] + $",\"fix\":{JsonSerializer.Serialize(fix)}}}";

    private static JsonElement SingleFinding(string runFile)
    {
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(runFile));
        return Assert.Single(evidence.RootElement.GetProperty("findings").EnumerateArray().ToArray()).Clone();
    }

    private string ReviewsDirectory() => Path.Combine(root.FullName, ".lockflight", "features", Feature, "reviews");

    private async Task<(ExitCode ExitCode, string Output)> RunAsync(params string[] arguments)
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
