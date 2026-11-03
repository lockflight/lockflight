using System.Text;
using System.Text.Json;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewReproductionRepairTests : ReviewReproductionScenario
{
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
}
