using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class GreenStepTests : IDisposable
{
    private static readonly RedGreenRecord Record = new()
    {
        BaselineCommit = "abc",
        BaselineAt = DateTimeOffset.UnixEpoch,
        BaselineTests = new HashSet<string>(StringComparer.Ordinal) { "T:A.Old", "T:A.Manual" },
        BaselineExplicit = new HashSet<string>(StringComparer.Ordinal) { "T:A.Manual" },
        TestProjects = ["tests"],
    };

    private static readonly IReadOnlySet<string> NoneExcluded = new HashSet<string>(StringComparer.Ordinal);

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-green-");

    [Fact]
    public void AKnownTestMissingFromTheReportMeansTheRunIsIncomplete()
    {
        string problem = Assert.Single(GreenStep.IncompleteRun(Record, [Test("Old"), Test("Manual", isExplicit: true)], [], abnormalExit: null, NoneExcluded));

        Assert.Contains("known tests missing from the run's report", problem, StringComparison.Ordinal);
        Assert.Contains("T:A.Old", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("Manual", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunnerThatDidNotFinishNormallyIsNotAGreen()
    {
        string problem = Assert.Single(GreenStep.IncompleteRun(Record, [Test("Old")], [Passed("Old")], "tests/T.dll (runner exit code 3)", NoneExcluded));

        Assert.Contains("did not finish normally: tests/T.dll (runner exit code 3)", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompleteNormalRunHasNoProblem() =>
        Assert.Empty(GreenStep.IncompleteRun(Record, [Test("Old")], [Passed("Old")], abnormalExit: null, NoneExcluded));

    [Fact]
    public void AKnownTestTheStopFilterLeftOutIsNotMissing() =>
        Assert.Empty(GreenStep.IncompleteRun(Record, [Test("Old")], [], abnormalExit: null, new HashSet<string>(StringComparer.Ordinal) { "T:A.Old" }));

    [Fact]
    public async Task ResettingAFeatureWithoutEvidenceSaysSo()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "f1"));
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, TimeProvider.System);

        CommandDescriptor reset = CommandRegistry.Match(["redgreen", "reset"])!;

        ExitCode exitCode = await new RedGreenCommand(context).RunAsync(new CommandArguments(reset, ["--feature", "f1"]), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("f1: no red/green evidence to discard; nothing was reset.", output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static TestCase Test(string method, bool isExplicit = false) => new("T", "A", method, [], null, isExplicit);

    private static ExecutedTest Passed(string method) => new($"T:A.{method}", $"A.{method}", TestOutcome.Pass, null, null);
}
