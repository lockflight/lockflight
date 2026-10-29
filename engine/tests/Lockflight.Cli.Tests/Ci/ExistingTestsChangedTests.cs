using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>Existing tests a pull request changes are run in their head version on the base's code (advisory).</summary>
[Trait("Category", "CiIntegration")]
public sealed class ExistingTestsChangedTests : IAsyncLifetime
{
    private const string Doubling = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private const string BaseTest = """
            [Fact]
            public void ShippingIsTwiceTheWeight() => Assert.Equal(4m, new Price().Shipping(2m));
        """;

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-existing-");
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(Doubling, BaseTest).CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("002-pricing", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AWeakenedExistingTestThatPassesOnTheBaseFailsTheAdvisoryCheck()
    {
        string head = await repository.WriteCode("    public decimal Shipping(decimal kilograms) => kilograms * 3m;", BaseTest.Replace("new Price().Shipping(2m)", "4m", StringComparison.Ordinal))
            .CommitAsync("feat: tripled", TestContext.Current.CancellationToken);

        JsonNode check = await CheckAsync(head);

        Assert.Equal("FAIL", check["verdict"]!.GetValue<string>());
        Assert.Equal("advisory", check["requirement"]!.GetValue<string>());
        Assert.Contains("no longer tells the old behaviour from the new", check["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangedExpectationThatFailsOnTheBaseIsRechecked()
    {
        string head = await repository.WriteCode("    public decimal Shipping(decimal kilograms) => kilograms * 3m;", BaseTest.Replace("4m", "6m", StringComparison.Ordinal))
            .CommitAsync("feat: tripled", TestContext.Current.CancellationToken);

        JsonNode check = await CheckAsync(head);

        Assert.Equal("PASS", check["verdict"]!.GetValue<string>());
        Assert.Equal("1 existing tests changed, each still failing on the base's code", check["why"]!.GetValue<string>());
    }

    [Fact]
    public async Task NoChangedTestPasses()
    {
        string head = await repository.Write("src/Shop/Notes.cs", "namespace Shop;\n\ninternal static class Notes;\n").CommitAsync("chore: notes", TestContext.Current.CancellationToken);

        Assert.Equal("no existing test changed", (await CheckAsync(head))["why"]!.GetValue<string>());
    }

    private async Task<JsonNode> CheckAsync(string head)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root, new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        return document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "tests.existingChanged")!;
    }
}
