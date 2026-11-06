using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Fix;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

[Trait("Category", "CiIntegration")]
public sealed class RedCommandTests : RedCommandScenario
{
    private const string SecondTest = """
            [Fact]
            public void ANegativeNetIsRefused() => Assert.Throws<ArgumentOutOfRangeException>(() => new Price().Total(-1m));
        """;

    [Fact]
    public async Task ABugFixRedIsAcceptedCommittedWithOneTrailerAndJudgedAlikeInCi()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string before = await repository.HeadAsync(cancellationToken);
        repository.WriteCode(Buggy, RoundingTest);

        (ExitCode exitCode, string output, string error) = await RedAsync();

        Assert.True(exitCode == ExitCode.Pass, $"{output}\n{error}");
        Assert.Contains("Production unchanged since the fork point", output, StringComparison.Ordinal);
        Assert.Contains("RED_ASSERT", output, StringComparison.Ordinal);
        Assert.Contains($"dotnet test", output, StringComparison.Ordinal);
        string red = await repository.HeadAsync(cancellationToken);
        Assert.NotEqual(before, red);
        Assert.Equal(before, await GitOutputAsync("rev-parse", "HEAD^1"));
        Assert.Equal($"test: red for {Branch}, cycle 1", await GitOutputAsync("log", "-1", "--format=%s"));
        CommitTrailers trailers = Assert.Single((await JudgeGit.HistoryAsync(repository.Root, before, red, cancellationToken))!);
        Assert.Equal(["1"], trailers.Red);
        Assert.Empty(await GitOutputAsync("status", "--porcelain", "--", "src", "tests"));
        Assert.Contains($"branch {Branch}: cycle 1, 1 tests", Ledger(), StringComparison.Ordinal);

        // With the fix committed, the CI judge re-runs the red commit: the same class for the same fresh test.
        string fix = await repository.WriteCode(Fixed, RoundingTest).CommitAsync("fix: round to the nearest unit", cancellationToken);
        DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-red-ci-");
        try
        {
            await TestConsole.RunAsync(["ci", "--platform", "github", "--head", fix, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root,
                new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(evidence.FullName, "evidence.json"), cancellationToken))!;
            JsonNode check = document["checks"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "red")!;
            Assert.Equal("PASS", check["verdict"]!.GetValue<string>());
            JsonNode test = Assert.Single(document["cycles"]![0]!["tests"]!.AsArray())!;
            Assert.Equal("RED_ASSERT", test["red"]!.GetValue<string>());
            Assert.Contains("TheTotalIsRoundedToTheNearestUnit", test["key"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        finally
        {
            TempDirectories.DeleteRecursively(evidence);
        }

        // A second bug: label 2, cycle 1's test is not new again; with the cache deleted, the earlier red is judged again.
        repository.WriteCode(Fixed, RoundingTest + "\n" + SecondTest);
        Directory.Delete(RedCache.DirectoryOf(repository.Root), recursive: true);

        (ExitCode dryExit, string dryOutput, string dryError) = await RedAsync("--dry-run");

        Assert.True(dryExit == ExitCode.Pass, $"{dryOutput}\n{dryError}");
        Assert.Contains("Judging the earlier red 1", dryOutput, StringComparison.Ordinal);
        Assert.Contains("'Lockflight-Red: 2'", dryOutput, StringComparison.Ordinal);
        Assert.Contains("ANegativeNetIsRefused", dryOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("RED_ASSERT   Shop.Tests.PriceTests.TheTotalIsRoundedToTheNearestUnit", dryOutput, StringComparison.Ordinal);
        Assert.Equal(fix, await repository.HeadAsync(cancellationToken));
        Assert.False(Directory.Exists(RedCache.DirectoryOf(repository.Root)), "a dry run writes no cache entry");

        (ExitCode secondExit, string secondOutput, string secondError) = await RedAsync("--message", "test: negative totals");

        Assert.True(secondExit == ExitCode.Pass, $"{secondOutput}\n{secondError}");
        Assert.Equal("test: negative totals", await GitOutputAsync("log", "-1", "--format=%s"));
        Assert.Equal("2", await GitOutputAsync("log", "-1", "--format=%(trailers:key=Lockflight-Red,valueonly)"));
    }

    [Fact]
    public async Task ATestWrittenAfterTheFixIsGreenBeforeAndNothingIsCommitted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.WriteCode(Fixed, tests: string.Empty).CommitAsync("fix: round to the nearest unit", cancellationToken);
        string before = await repository.HeadAsync(cancellationToken);
        repository.WriteCode(Fixed, RoundingTest);

        (ExitCode exitCode, string output, string error) = await RedAsync();

        Assert.True(exitCode == ExitCode.Fail, $"{output}\n{error}");
        Assert.Contains("GREEN_BEFORE", output, StringComparison.Ordinal);
        Assert.Contains("Production files in this red", output, StringComparison.Ordinal);
        Assert.Equal(before, await repository.HeadAsync(cancellationToken));
        Assert.Contains($"branch {Branch}: cycle 1: GREEN_BEFORE", Ledger(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHookThatRefusesLeavesTheRedAcceptedAndOneThatRewritesTheTreeUndoesTheCommit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string before = await repository.HeadAsync(cancellationToken);
        repository.WriteCode(Buggy, RoundingTest);
        Hook("#!/bin/sh\nexit 1\n");

        (ExitCode refusedExit, string refusedOutput, string refusedError) = await RedAsync();

        Assert.True(refusedExit == ExitCode.Pass, $"{refusedOutput}\n{refusedError}");
        Assert.Contains("--no-verify", refusedOutput, StringComparison.Ordinal);
        Assert.Equal(before, await repository.HeadAsync(cancellationToken));

        Hook("#!/bin/sh\necho '// formatted' >> src/Shop/Price.cs\ngit add src/Shop/Price.cs\n");

        (ExitCode changedExit, string changedOutput, string changedError) = await RedAsync();

        Assert.True(changedExit == ExitCode.Fail, $"{changedOutput}\n{changedError}");
        Assert.Contains("a commit hook changed the red's tree", changedError, StringComparison.Ordinal);
        Assert.Equal(before, await repository.HeadAsync(cancellationToken));
    }

    [Fact]
    public async Task TestsThatDoNotCompileAreCompileRedAndNothingIsCommitted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string before = await repository.HeadAsync(cancellationToken);
        repository.WriteCode(Buggy, "    [Fact]\n    public void Missing() => Assert.Equal(1m, new Price().Discount(1m));");

        (ExitCode exitCode, string output, string error) = await RedAsync();

        Assert.True(exitCode == ExitCode.Fail, $"{output}\n{error}");
        Assert.Contains("COMPILE_RED", output, StringComparison.Ordinal);
        Assert.Equal(before, await repository.HeadAsync(cancellationToken));
        Assert.Contains("redgreen:red:COMPILE_RED", Ledger(), StringComparison.Ordinal);
    }

    private void Hook(string script)
    {
        string path = Path.Combine(repository.Root, ".git", "hooks", "pre-commit");
        repository.Write(".git/hooks/pre-commit", script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
