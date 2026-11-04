using System.Runtime.CompilerServices;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class JudgeWorkflowMigrationTests
{
    [Fact]
    public void ALockflight02GatesWorkflowIsReplacedByTheCurrentTemplate()
    {
        JudgeWorkflowPlan plan = JudgeWorkflowMigration.Plan(Lockflight02(), developmentPin: false);

        Assert.Equal(JudgeWorkflowStatus.Recognised, plan.Status);
        Assert.Equal(Templates.Workflow(["10.0.x"]), plan.Replacement);
    }

    [Fact]
    public void AGatesWorkflowThatUpdateAlreadyMovedToTheEngineLockIsRecognisedToo()
    {
        string moved = CiStepMigration.Migrate(Lockflight02())!;

        Assert.Equal(JudgeWorkflowStatus.Recognised, JudgeWorkflowMigration.Plan(moved, developmentPin: false).Status);
    }

    [Fact]
    public void WhatLockflightWroteBetweenItsMarkersMayDiffer()
    {
        string moved = CiStepMigration.Migrate(Lockflight02())!;
        string begin = CiShellScripts.InstallBeginMarker;
        int start = moved.IndexOf(begin, StringComparison.Ordinal) + begin.Length;
        string edited = moved.Insert(start, "\n          echo an older install step");

        Assert.Equal(JudgeWorkflowStatus.Recognised, JudgeWorkflowMigration.Plan(edited, developmentPin: false).Status);
    }

    [Theory]
    [InlineData(0, LegacyGitHubWorkflows.PullRequestTrigger, "on:\n  pull_request:\n")]
    [InlineData(0, LegacyGitHubWorkflows.ManualTrigger, "lockflight init --ci manual")]
    [InlineData(1, LegacyGitHubWorkflows.PullRequestTrigger, "on:\n  pull_request:\n")]
    [InlineData(1, LegacyGitHubWorkflows.ManualTrigger, "lockflight init --ci manual")]
    [InlineData(1, LegacyGitHubWorkflows.PreviewTrigger, "on:\n  pull_request:\n")]
    [InlineData(2, LegacyGitHubWorkflows.ManualTrigger, "lockflight init --ci manual")]
    public void EveryLockflight02TemplateIsRecognisedWithItsTriggerAndChannels(int template, string trigger, string expectedTrigger)
    {
        string legacy = LegacyGitHubWorkflows.All[template]
            .Replace(LegacyGitHubWorkflows.DotnetVersionsPlaceholder, $"|\n{LegacyGitHubWorkflows.DotnetVersionIndent}8.0.x\n{LegacyGitHubWorkflows.DotnetVersionIndent}10.0.x", StringComparison.Ordinal)
            .Replace(LegacyGitHubWorkflows.PullRequestTrigger, trigger, StringComparison.Ordinal);

        JudgeWorkflowPlan plan = JudgeWorkflowMigration.Plan(legacy, developmentPin: false);

        Assert.Equal(JudgeWorkflowStatus.Recognised, plan.Status);
        Assert.Contains("mode: judge", plan.Replacement, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: |\n            8.0.x\n            10.0.x\n", plan.Replacement, StringComparison.Ordinal);
        Assert.Contains(expectedTrigger, plan.Replacement, StringComparison.Ordinal);
    }

    [Fact]
    public void AProjectPinnedToADevelopmentBuildGetsTheWorkflowThatRunsOnlyByHand()
    {
        JudgeWorkflowPlan plan = JudgeWorkflowMigration.Plan(Lockflight02(), developmentPin: true);

        Assert.Equal(Templates.Workflow(["10.0.x"], CiTrigger.Manual, developmentPin: true), plan.Replacement);
    }

    [Fact]
    public void AUnitRepositorysWorkflowRunsTheGatesAloneByDesign() =>
        Assert.Equal(JudgeWorkflowStatus.NotGatesOnly, JudgeWorkflowMigration.Plan(Templates.UnitWorkflow(["10.0.x"]), developmentPin: false).Status);

    [Fact]
    public void AnEditOutsideLockflightsOwnLinesRefusesTheReplacement()
    {
        string edited = Lockflight02().Replace("      - name: Gates\n", "      - run: ./scripts/our-own-check.sh\n      - name: Gates\n", StringComparison.Ordinal);

        JudgeWorkflowPlan plan = JudgeWorkflowMigration.Plan(edited, developmentPin: false);

        Assert.Equal(JudgeWorkflowStatus.Edited, plan.Status);
        Assert.Null(plan.Replacement);
    }

    [Fact]
    public void TheCiJudgesWorkflowAndAnUnrelatedFileAreNotGatesOnly()
    {
        Assert.Equal(JudgeWorkflowStatus.NotGatesOnly, JudgeWorkflowMigration.Plan(Templates.Workflow(["10.0.x"]), developmentPin: false).Status);
        Assert.Equal(JudgeWorkflowStatus.NotGatesOnly, JudgeWorkflowMigration.Plan("name: build\non: push\n", developmentPin: false).Status);
    }

    [Fact]
    public void AFileWithWindowsLineEndingsKeepsThem()
    {
        JudgeWorkflowPlan plan = JudgeWorkflowMigration.Plan(Lockflight02().Replace("\n", "\r\n", StringComparison.Ordinal), developmentPin: false);

        Assert.Equal(JudgeWorkflowStatus.Recognised, plan.Status);
        Assert.Equal(Templates.Workflow(["10.0.x"]).Replace("\n", "\r\n", StringComparison.Ordinal), plan.Replacement);
    }

    /// <summary>The GitHub workflow Lockflight 0.2.0-alpha.9's <c>init</c> wrote.</summary>
    private static string Lockflight02([CallerFilePath] string path = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "CiMigrationCases", "github.yml")).ReplaceLineEndings("\n");
}
