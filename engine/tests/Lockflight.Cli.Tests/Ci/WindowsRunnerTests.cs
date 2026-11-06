using Lockflight.Cli.Ci;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Tests.Projects;

namespace Lockflight.Cli.Tests.Ci;

public sealed class WindowsRunnerTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-windows-runner-");

    [Fact]
    public void ATreeWithADesktopTestProjectCannotBeJudgedOffWindows()
    {
        DesktopProjects.WriteWpfTests(root.FullName);
        DesktopProjects.WriteServerTests(root.FullName);

        string? why = WindowsRunner.Why(root.FullName, onWindows: false);

        Assert.NotNull(why);
        Assert.Contains($"{DesktopProjects.WpfTests} targets Windows", why, StringComparison.Ordinal);
        Assert.DoesNotContain("Server.Tests", why, StringComparison.Ordinal);
    }

    [Fact]
    public void OnWindowsOrWithoutADesktopTestProjectTheTreeIsBuilt()
    {
        DesktopProjects.WriteServerTests(root.FullName);
        Assert.Null(WindowsRunner.Why(root.FullName, onWindows: false));

        DesktopProjects.WriteWpfTests(root.FullName);
        Assert.Null(WindowsRunner.Why(root.FullName, onWindows: true));
    }

    [Fact]
    public void EveryCheckThatBuildsIsAnErrorThatNeedsAWindowsRunnerAndTheOthersAreStillJudged()
    {
        JudgePlan plan = CiEvidenceTests.Run().Plan;
        JudgeFindings findings = JudgeFindings.None with { NotBuilt = (CheckVerdict.Error, ReasonCode.NeedsWindowsRunner, "tests/Client.Tests/Client.Tests.csproj targets Windows") };

        IReadOnlyList<CheckResult> checks = JudgeChecks.Decide(plan, findings);

        CheckId[] building = [CheckId.Red, CheckId.TestsSinceRed, CheckId.TestsExistingChanged, CheckId.TestsHostTampering, CheckId.Trace, CheckId.Gates, CheckId.Green];
        Assert.All(checks.Where(check => building.Contains(check.Id) && check.Level != CheckLevel.Off), check =>
        {
            Assert.Equal(CheckVerdict.Error, check.Verdict);
            Assert.Equal(ReasonCode.NeedsWindowsRunner, check.Reason);
        });
        Assert.Contains(checks, check => check.Id == CheckId.Green && check.Reason == ReasonCode.NeedsWindowsRunner);
        Assert.Contains(checks, check => check.Id == CheckId.Red && check.Reason == ReasonCode.NeedsWindowsRunner);
        Assert.DoesNotContain(checks, check => !building.Contains(check.Id) && check.Reason == ReasonCode.NeedsWindowsRunner);
        Assert.Contains("lockflight update --project", ReasonCatalogue.Next(ReasonCode.NeedsWindowsRunner), StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
