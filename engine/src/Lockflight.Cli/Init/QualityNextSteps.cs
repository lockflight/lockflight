using Lockflight.Cli.Hosting;
using Lockflight.Cli.Quality;

namespace Lockflight.Cli.Init;

/// <summary>What the person does after <c>init --quality</c> or <c>--architecture-tests</c> wrote or proposed something.</summary>
internal static class QualityNextSteps
{
    public const string GuideUrl = "https://github.com/lockflight/lockflight/blob/main/docs/guides/code-quality.md";

    /// <param name="steps">Every file init looked after.</param>
    /// <param name="proposals">The changes proposed to files the team already has.</param>
    /// <param name="architectureProject">The architecture test project's path; null when it was not asked for.</param>
    /// <param name="level">The install level: only the plugin brings the <c>/lockflight:</c> commands the hand-off may name.</param>
    public static IReadOnlyList<string> For(InitQuality quality, IReadOnlyList<InitStep> steps, IReadOnlyList<QualityFile> proposals, string? architectureProject, InstallLevel level)
    {
        List<string> lines = [];
        bool packChanged = steps.Any(step => step.Outcome != FileOutcome.Unchanged && IsPackFile(step.RelativePath))
            || proposals.Any(proposal => IsPackFile(proposal.RelativePath));
        if (quality.Analyzers && packChanged)
        {
            lines.Add(
                "Quality pack: the recommended analyzers and the code metrics rules add build warnings, and the build gate fails on every new one. "
                + $"Before you commit it, apply any patch above, then accept today's warnings: {HumanOnlyCommands.HandOff("baseline build", level == InstallLevel.Plugin)}. "
                + $"Merge that baseline in its own pull request first (CI takes baselines from the base branch), then the pack: {GuideUrl}");
        }

        if (architectureProject is not null && steps.Any(step => step.RelativePath == architectureProject && step.Outcome == FileOutcome.Created))
        {
            string directory = architectureProject[..architectureProject.LastIndexOf('/')];
            lines.Add(
                $"Architecture tests: add {architectureProject} to your solution (dotnet sln add {architectureProject}) so the gates build and run it, "
                + $"and `{directory}/**` to protected: in lockflight.yaml so the agent cannot loosen its rules. A rule the code breaks today fails the test gate: "
                + $"fix it, or accept it with baseline tests as above: {GuideUrl}#architecture-tests");
        }

        return lines;
    }

    private static bool IsPackFile(string relativePath) =>
        relativePath is QualityPack.EditorConfigPath or QualityPack.BuildPropsPath or QualityPack.CodeMetricsPath;
}
