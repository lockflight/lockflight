using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Processes.Survivors;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Tests.Ci;

public sealed class SurvivorVerdictTests
{
    [Fact]
    public void AProcessThatCouldNotBeStoppedMakesGreenAndGatesError()
    {
        JudgeRun run = Judged(new SweptProcess(4711, "/usr/bin/sudo", "sudo setsid sh -c sleep 600", "uid 0", SurvivorOutcome.NotStopped));

        CheckResult green = run.Checks.Single(check => check.Id == CheckId.Green);
        CheckResult gates = run.Checks.Single(check => check.Id == CheckId.Gates);
        Assert.Equal((CheckVerdict.Error, ReasonCode.SurvivorNotStopped), (green.Verdict, green.Reason));
        Assert.Equal((CheckVerdict.Error, ReasonCode.SurvivorNotStopped), (gates.Verdict, gates.Reason));
        Assert.Equal(CheckVerdict.Error, run.Verdict);
    }

    [Fact]
    public void AStoppedProcessOrContainerLeavesTheVerdictAsItWas()
    {
        JudgeRun clean = Judged();
        JudgeRun stopped = Judged(
            new SweptProcess(4711, "/usr/bin/sleep", "sleep 600", "runner", SurvivorOutcome.Stopped),
            new SweptProcess(0, "alpine:3.20", "sleep 600", null, SurvivorOutcome.Stopped) { Kind = SurvivorKind.Container, Container = new string('c', 64) });

        Assert.Equal(clean.Checks.Select(check => (check.Id, check.Verdict, check.Reason)), stopped.Checks.Select(check => (check.Id, check.Verdict, check.Reason)));
    }

    [Fact]
    public void AContainerIsWrittenWithItsKindAndId()
    {
        JudgeRun run = Judged(new SweptProcess(0, "alpine:3.20", "sleep 600", null, SurvivorOutcome.NotStopped) { Kind = SurvivorKind.Container, Container = new string('c', 64) });

        JsonNode survivor = CiEvidence.Build(run, "0.3.0", "linux-x64", null)["processes"]!["survivors"]![0]!;

        Assert.Equal("container", survivor["kind"]!.GetValue<string>());
        Assert.Equal(new string('c', 64), survivor["container"]!.GetValue<string>());
        Assert.Equal("not-stopped", survivor["outcome"]!.GetValue<string>());
        Assert.Equal(1, CiEvidence.Build(run, "0.3.0", "linux-x64", null)["processes"]!["notStopped"]!.GetValue<int>());
    }

    [Fact]
    public void TheCommentListsWhatWasLeftRunningAsDataAndOpensWhenOneWasNotStopped()
    {
        JudgeRun run = Judged(
            new SweptProcess(4711, "/usr/bin/sh", "sh -c `curl x` @maintainers <b>", "runner", SurvivorOutcome.NotStopped),
            new SweptProcess(4712, "/usr/bin/dotnet", "dotnet MSBuild.dll /nodemode:1", "runner", SurvivorOutcome.Exited));

        string comment = CommentRenderer.Render(CiEvidence.Build(run, "0.3.0", "linux-x64", null), CommentRenderer.MaxCommentLength);

        Assert.Contains("<details open><summary>Processes left running (1)</summary>", comment, StringComparison.Ordinal);
        Assert.Contains("1 could not be stopped, so `green` and `gates` are ERROR.", comment, StringComparison.Ordinal);
        Assert.Contains("- not-stopped: `sh -c 'curl x' @maintainers <b>` (process; head, after `dotnet build`)", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("nodemode", comment, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommentSaysWhenProcessesWereNotChecked()
    {
        JudgeRun run = CiEvidenceTests.Run() with
        {
            Findings = JudgeFindings.None with { Processes = new ProcessesReport(SweepMechanism.None, "not on this system", 0, [], 0, []) },
        };

        string comment = CommentRenderer.Render(CiEvidence.Build(run, "0.3.0", "linux-x64", null), CommentRenderer.MaxCommentLength);

        Assert.Contains("> Processes left running were not checked: not on this system.", comment, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommentShowsWhyContainersWereNotCheckedWithoutAnError()
    {
        JudgeRun run = CiEvidenceTests.Run() with
        {
            Findings = JudgeFindings.None with { Processes = new ProcessesReport(SweepMechanism.Subreaper, "1 Docker container was not checked", 4, [], 0, []) },
        };

        string comment = CommentRenderer.Render(CiEvidence.Build(run, "0.3.0", "linux-x64", null), CommentRenderer.MaxCommentLength);

        Assert.Contains("> 1 Docker container was not checked.", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("are ERROR", comment, StringComparison.Ordinal);
    }

    private static JudgeRun Judged(params SweptProcess[] found)
    {
        JudgeRun run = CiEvidenceTests.Run();
        ProcessesReport processes = new(
            SweepMechanism.Subreaper, null, 12, [.. found.Select(process => new Survivor(SweepRegion.Head, "dotnet build", process))], 0, [])
        {
            NotStopped = found.Count(process => process.Outcome == SurvivorOutcome.NotStopped),
        };
        JudgeFindings findings = run.Findings with { Processes = processes };
        return run with { Findings = findings, Checks = JudgeChecks.Decide(run.Plan, findings) };
    }
}
