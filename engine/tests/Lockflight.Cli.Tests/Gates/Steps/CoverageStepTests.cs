using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Gates.Steps;

public sealed class CoverageStepTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-coverage-");

    [Fact]
    public async Task UnconfiguredCoverageIsNotApplicableRatherThanAPass()
    {
        GateRunContext run = new(projectRoot.FullName, projectRoot.FullName, GateProfile.PullRequest, GateSettings.Default, BaseRef: null, TimeProvider.System);

        GateStep step = await CoverageStep.RunAsync(run, DateTimeOffset.UnixEpoch, resultsDirectory: null, TestContext.Current.CancellationToken);

        Assert.Equal(Verdict.NotApplicable, step.Verdict);
        Assert.Contains("not judged", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void NotApplicableIsSpelledNaLetsTheRunContinueAndExitsZero()
    {
        Assert.Equal("N/A", VerdictNames.ToContractName(Verdict.NotApplicable));
        Assert.True(VerdictNames.AllowsNextStep(Verdict.NotApplicable));
        Assert.False(VerdictNames.AllowsNextStep(Verdict.NotEarned));
        Assert.Equal(ExitCode.Pass, VerdictNames.ToExitCode(Verdict.NotApplicable));
    }

    [Fact]
    public void TheDeterministicSourceRootIsTheNearestRepositoryAboveTheTarget()
    {
        string unit = Path.Combine(projectRoot.FullName, "services", "orders");
        Directory.CreateDirectory(Path.Combine(unit, ".git"));
        Directory.CreateDirectory(Path.Combine(unit, "src"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".git"), "gitdir: elsewhere");

        Assert.Equal(unit, CoverageStep.RepositoryRootOf(Path.Combine(unit, "src")));
        Assert.Equal(projectRoot.FullName, CoverageStep.RepositoryRootOf(Path.Combine(projectRoot.FullName, "web")));
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
