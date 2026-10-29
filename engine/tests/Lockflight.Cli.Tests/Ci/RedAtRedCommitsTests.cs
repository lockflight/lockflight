using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The judge re-runs each red commit on a real build: its new tests must fail in production code there, as <c>redgreen red</c>
/// demands locally, whatever the trailer claims.
/// </summary>
[Trait("Category", "CiIntegration")]
public sealed class RedAtRedCommitsTests : IAsyncLifetime
{
    private const string Stub = "    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();";
    private const string Implementation = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private const string ShippingTest = """
            [Fact]
            [Trait("Requirement", "FR-001")]
            public void ShippingIsTwicTheWeight() => Assert.Equal(4m, new Price().Shipping(2m));
        """;

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-red-");
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(production: string.Empty, tests: string.Empty).CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TwoCyclesWhoseTestsFailInProductionCodeAreAccepted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string TaxStub = "    public decimal Tax(decimal net) => throw new NotImplementedException();";
        const string TaxTest = """
                [Fact]
                public void TaxIsAFifth() => Assert.Equal(20m, new Price().Tax(100m));
            """;
        await repository.WriteCode(Stub, ShippingTest).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.WriteCode(Implementation, ShippingTest).CommitAsync("feat: US1", cancellationToken);
        await repository.WriteCode(Implementation + "\n" + TaxStub, ShippingTest + "\n" + TaxTest).CommitAsync("test: red for US2", cancellationToken, null, "Lockflight-Red: 2");
        string head = await repository.WriteCode(Implementation + "\n    public decimal Tax(decimal net) => net / 5m;", ShippingTest + "\n" + TaxTest).CommitAsync("feat: US2", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Check(document, "red")["verdict"]!.GetValue<string>());
        Assert.Equal("2 cycles, 2 tests, each red in production code", Check(document, "red")["why"]!.GetValue<string>());
        JsonArray cycles = document["cycles"]!.AsArray();
        Assert.Equal(["RED_STUB", "RED_STUB"], cycles.SelectMany(cycle => cycle!["tests"]!.AsArray()).Select(test => test!["red"]!.GetValue<string>()));
        Assert.Equal(["FR-001"], cycles[0]!["tests"]![0]!["requirements"]!.AsArray().Select(requirement => requirement!.GetValue<string>()));
        Assert.Equal(["head", "base", "red 1", "red 2"], document["builds"]!.AsArray().Select(build => build!["purpose"]!.GetValue<string>()));
        Assert.Equal("PASS", Check(document, "green")["verdict"]!.GetValue<string>());
        Assert.Equal(["pass", "pass"], cycles.SelectMany(cycle => cycle!["tests"]!.AsArray()).Select(test => test!["atHead"]!.GetValue<string>()));

        // Cycle 2 added a test to cycle 1's class: cycle 1's test is still the one that went red.
        JsonNode first = cycles[0]!["tests"]![0]!;
        Assert.NotNull(first["fingerprintAtRed"]);
        Assert.Equal(first["fingerprintAtRed"]!.GetValue<string>(), first["fingerprintAtHead"]!.GetValue<string>());
        Assert.Equal("2 unchanged, 0 amends accepted", Check(document, "tests.sinceRed")["why"]!.GetValue<string>());
    }

    [Fact]
    public async Task ARedCommitThatHoldsTheImplementationIsGreenBeforeAndFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.WriteCode(Implementation, ShippingTest).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Check(document, "red")["verdict"]!.GetValue<string>());
        Assert.Contains("GREEN_BEFORE", Check(document, "red")["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedCommitWhoseTestsDoNotCompileIsCompileRed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.WriteCode(string.Empty, ShippingTest).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Check(document, "red")["verdict"]!.GetValue<string>());
        Assert.Contains("COMPILE_RED", Check(document, "red")["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("compile-red", document["cycles"]![0]!["build"]!.GetValue<string>());
    }

    [Fact]
    public async Task ARedCommitWithoutNewTestsFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.WriteCode(Stub, string.Empty).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Check(document, "red")["verdict"]!.GetValue<string>());
        Assert.Contains("adds no tests", Check(document, "red")["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedThatDeletesABaseTestFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository.WriteCode(Stub, ShippingTest);
        string tests = File.ReadAllText(Path.Combine(repository.Root, CiSolution.TestsFile)).Replace("public void ABaseTestPasses() => Assert.True(true);", "public void Other() { }", StringComparison.Ordinal);
        string head = await repository.Write(CiSolution.TestsFile, tests).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Check(document, "red")["verdict"]!.GetValue<string>());
        Assert.Contains("tests disappeared since the fork point", Check(document, "red")["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWorktreeForACommitWithoutDirectoryBuildPropsDoesNotPickUpTheCheckouts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.WriteCode(Stub, ShippingTest).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.WriteCode(Implementation, ShippingTest)
            .Write("Directory.Build.props", "<Project>\n  <Target Name=\"Break\" BeforeTargets=\"Build\">\n    <Error Text=\"the checkout's props\" />\n  </Target>\n</Project>\n")
            .CommitAsync("feat: US1", cancellationToken);

        // Locally the worktree lives in the repository's git directory, below the checkout that now holds the breaking props.
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["ci", "--base", main], string.Empty, repository.Root);

        JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(repository.Root, ".lockflight", "local", "ci", "last.json"), cancellationToken))!;
        Assert.True(Check(document, "red")["verdict"]!.GetValue<string>() == "PASS", $"exit {(int)exitCode}: {output}\n{error}");
    }

    [Fact]
    public async Task ATestThatFailsAtHeadFailsGreenAndOneThatPassesOnARetryIsAWarning()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string marker = Path.Combine(evidence.FullName, "flaked-once").Replace("\\", "/", StringComparison.Ordinal);
        string flaky = $$"""
                [Fact]
                public void FailsOnce()
                {
                    if (!File.Exists("{{marker}}")) { File.WriteAllText("{{marker}}", "x"); Assert.Fail("first run"); }
                }
            """;
        await repository.WriteCode(Stub, ShippingTest).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode(Implementation, ShippingTest + "\n" + flaky).CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Check(document, "green")["verdict"]!.GetValue<string>());
        Assert.Contains("passed only when run again: Shop.Tests:Shop.Tests.PriceTests.FailsOnce", Check(document, "green")["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(["Shop.Tests:Shop.Tests.PriceTests.FailsOnce"], document["testsWithoutRed"]!.AsArray().Select(test => test!.GetValue<string>()));

        string broken = await repository.WriteCode("    public decimal Shipping(decimal kilograms) => kilograms * 3m;", ShippingTest).CommitAsync("fix: wrong", cancellationToken);
        document = await JudgeAsync(broken);

        Assert.Equal("FAIL", Check(document, "green")["verdict"]!.GetValue<string>());
        Assert.Contains("cycle tests not passing", Check(document, "green")["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("FAIL", document["contexts"]!["gates"]!.GetValue<string>());
    }

    private async Task<JsonNode> JudgeAsync(string head)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root, new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(evidence.FullName, "evidence.json"), TestContext.Current.CancellationToken))!;
    }

    private static JsonNode Check(JsonNode document, string id) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == id)!;
}
