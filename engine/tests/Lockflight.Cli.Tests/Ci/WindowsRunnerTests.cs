using Lockflight.Cli.Ci;
using Lockflight.Cli.Gates.WindowsTargets;
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

        string? why = WindowsRunner.Why(root.FullName, BuildScope.OfRepository(root.FullName), onWindows: false);

        Assert.NotNull(why);
        Assert.Contains($"{DesktopProjects.WpfTests} targets Windows", why, StringComparison.Ordinal);
        Assert.DoesNotContain("Server.Tests", why, StringComparison.Ordinal);
    }

    [Fact]
    public void OnWindowsOrWithoutADesktopTestProjectTheTreeIsBuilt()
    {
        DesktopProjects.WriteServerTests(root.FullName);
        Assert.Null(WindowsRunner.Why(root.FullName, BuildScope.OfRepository(root.FullName), onWindows: false));

        DesktopProjects.WriteWpfTests(root.FullName);
        Assert.Null(WindowsRunner.Why(root.FullName, BuildScope.OfRepository(root.FullName), onWindows: true));
    }

    [Fact]
    public void AWpfAppWithPlainTestsIsJudgedOffWindowsAndBuildsWithWindowsTargeting()
    {
        DesktopProjects.Write(root.FullName, "src/Client/Client.csproj", "<PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup>", package: "CommunityToolkit.Mvvm");
        DesktopProjects.WriteServerTests(root.FullName);
        BuildScope scope = new(root.FullName, BuildFile: null);

        Assert.Null(WindowsRunner.Why(root.FullName, [scope], onWindows: false));
        Assert.Equal([WindowsTargeting.BuildProperty], WindowsTargeting.BuildArguments(scope, onWindows: false));
        Assert.Empty(WindowsTargeting.BuildArguments(scope, onWindows: true));
    }

    [Fact]
    public void AWpfTestProjectOutsideWhatTheJudgeBuildsNeedsNoWindowsRunner()
    {
        DesktopProjects.WriteServerTests(root.FullName);
        DesktopProjects.WriteOutsideSolution(root.FullName, "samples/Desk/Desk.Tests.csproj", "<PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup>");

        Assert.Null(WindowsRunner.Why(root.FullName, BuildScope.OfRepository(root.FullName), onWindows: false));
        Assert.Empty(WindowsTargeting.BuildArguments(new BuildScope(root.FullName, BuildFile: null), onWindows: false));
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
