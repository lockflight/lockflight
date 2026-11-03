using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class RedAtRedCommitsGreenCheckTests : RedAtRedCommitsScenario
{
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

    [Fact]
    public async Task ABaseTestSkippedAtRunTimeOnTheBaseTooIsNoNewSkipButOneSkippedOnlyAtHeadFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Docker = "tests/Shop.Tests/DockerTests.cs";
        const string NeedsDocker = "namespace Shop.Tests;\n\npublic sealed class DockerTests\n{\n    [Fact]\n    public void NeedsDocker() => Assert.Skip(\"no Docker here\");\n\n    [Fact]\n    public void Other() => Assert.True(true);\n}\n";
        await repository.CheckoutAsync("main", cancellationToken);
        main = await repository.Write(Docker, NeedsDocker).CommitAsync("test: docker", cancellationToken);
        await repository.GitAsync(["checkout", "-q", "-B", "001-shipping"], cancellationToken);
        await repository.WriteCode(Stub, ShippingTest).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode(Implementation, ShippingTest).CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Check(document, "green")["verdict"]!.GetValue<string>());

        string skipped = await repository.Write(Docker, NeedsDocker.Replace("public void Other() => Assert.True(true);", "public void Other() => Assert.Skip(\"later\");", StringComparison.Ordinal))
            .CommitAsync("test: skip other", cancellationToken);
        document = await JudgeAsync(skipped);

        Assert.Equal("FAIL", Check(document, "green")["verdict"]!.GetValue<string>());
        Assert.Contains("new skipped tests: Shop.Tests:Shop.Tests.DockerTests.Other", Check(document, "green")["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
