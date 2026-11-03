using System.Text.Json.Nodes;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class RedAtRedCommitsRedCheckTests : RedAtRedCommitsScenario
{
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
        string head = await repository.WriteCode("    public int Other => 1;", ShippingTest).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");

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
}
