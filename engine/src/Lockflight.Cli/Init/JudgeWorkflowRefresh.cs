using System.Text.RegularExpressions;

namespace Lockflight.Cli.Init;

/// <summary>
/// Brings the CI judge's GitHub workflow to this engine's template, whatever changed in it: the action pin, a step, a job. The
/// file is recognised as Lockflight's when, after the line edits <see cref="CiStepMigration"/> makes, it is this release's
/// template or one of <see cref="ReleasedJudgeWorkflows"/> rendered with the file's own .NET channels, trigger, runners and
/// workspace shell clone, and with any action pin (each release pins its own). The replacement keeps those, and the line endings.
/// Any other difference is a person's edit, and the file is only compared: the caller shows the replacement and leaves the file.
/// </summary>
internal static partial class JudgeWorkflowRefresh
{
    private const string JudgeMode = "mode: judge";

    private const string DotnetVersionKey = "dotnet-version:";

    private const string ShellCloneInput = "shell-clone: \"true\"";

    private const string ActionPlaceholder = "lockflight/lockflight/action@<pin>";

    /// <summary>Words of the comment above the trigger <c>init</c> writes while a development build is pinned.</summary>
    private const string DevelopmentBuildNote = "pins a development build";

    /// <summary>The triggers a template is rendered with: on pull requests, by hand, and by hand while a development build is pinned.</summary>
    private static readonly (CiTrigger Trigger, bool DevelopmentPin)[] Triggers =
        [(CiTrigger.PullRequest, false), (CiTrigger.Manual, false), (CiTrigger.Manual, true)];

    public static JudgeWorkflowRefreshPlan Plan(string text)
    {
        if (!text.Contains(JudgeMode, StringComparison.Ordinal))
        {
            return JudgeWorkflowRefreshPlan.NotJudge;
        }

        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string lines = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        string migrated = CiStepMigration.Migrate(lines) ?? lines;
        string[] channels = Channels(migrated) ?? ["10.0.x"];
        bool shell = migrated.Contains(ShellCloneInput, StringComparison.Ordinal);
        Dictionary<string, string?> runners = JudgeRunner.BuildingJobs.ToDictionary(job => job, job => JudgeRunner.RunsOn(migrated, job), StringComparer.Ordinal);
        string normalised = Normalised(migrated);
        foreach ((CiTrigger trigger, bool developmentPin) in Triggers)
        {
            foreach (string template in ReleasedJudgeWorkflows.All)
            {
                if (Normalised(Render(template, channels, trigger, developmentPin, runners, shell)) == normalised)
                {
                    return Replaced(text, Current(channels, trigger, developmentPin, runners, shell), newline);
                }
            }

            string current = Current(channels, trigger, developmentPin, runners, shell);
            if (Normalised(current) == normalised)
            {
                return Replaced(text, current, newline);
            }
        }

        (CiTrigger kept, bool pin) = TriggerOf(migrated);
        return new JudgeWorkflowRefreshPlan(JudgeWorkflowRefreshStatus.Edited, Current(channels, kept, pin, runners, shell).Replace("\n", newline, StringComparison.Ordinal));
    }

    private static JudgeWorkflowRefreshPlan Replaced(string text, string replacement, string newline)
    {
        string written = replacement.Replace("\n", newline, StringComparison.Ordinal);
        return new JudgeWorkflowRefreshPlan(written == text ? JudgeWorkflowRefreshStatus.Current : JudgeWorkflowRefreshStatus.Outdated, written);
    }

    private static string Current(string[] channels, CiTrigger trigger, bool developmentPin, Dictionary<string, string?> runners, bool shell) =>
        Shaped(Templates.Workflow(channels, trigger, developmentPin), runners, shell);

    private static string Render(string template, string[] channels, CiTrigger trigger, bool developmentPin, Dictionary<string, string?> runners, bool shell) =>
        Shaped(Templates.WorkflowOf(template, channels, trigger, developmentPin), runners, shell);

    /// <summary>
    /// The rendered template with the file's runners (Windows ones with the bash default <see cref="JudgeRunner"/> adds, a label
    /// chosen by hand as it is) and, for a unit of a workspace shell, the action's shell clone.
    /// </summary>
    private static string Shaped(string workflow, Dictionary<string, string?> runners, bool shell)
    {
        string shaped = JudgeRunner.Apply(workflow, windows: runners["judge"] is string judge && JudgeRunner.IsWindows(judge));
        foreach ((string job, string? label) in runners)
        {
            if (label is not null)
            {
                shaped = JudgeRunner.WithRunsOn(shaped, job, label);
            }
        }

        return shell ? CiShellPointer.ForUnit(new CiFile(Templates.WorkflowPath, shaped), CiPlatform.GitHub).Content : shaped;
    }

    /// <summary>The trigger the file seems to have, for the replacement shown next to an edited file.</summary>
    private static (CiTrigger Trigger, bool DevelopmentPin) TriggerOf(string workflow) =>
        workflow.Contains("\n  workflow_dispatch:", StringComparison.Ordinal) && !workflow.Contains("\n  pull_request:", StringComparison.Ordinal)
            ? (CiTrigger.Manual, workflow.Contains(DevelopmentBuildNote, StringComparison.Ordinal))
            : (CiTrigger.PullRequest, false);

    /// <summary>The text with every action pin the same, without trailing blank lines (an editor may add or drop the last newline).</summary>
    private static string Normalised(string text) => ActionPin().Replace(text, ActionPlaceholder).TrimEnd('\n');

    /// <summary>The .NET channels of the first <c>dotnet-version:</c> (the judge job's): its value, or the lines of its block list.</summary>
    private static string[]? Channels(string text)
    {
        string[] lines = text.Split('\n');
        int key = Array.FindIndex(lines, line => line.TrimStart().StartsWith(DotnetVersionKey, StringComparison.Ordinal));
        if (key < 0)
        {
            return null;
        }

        string value = lines[key].TrimStart()[DotnetVersionKey.Length..].Trim();
        if (value != "|")
        {
            return value.Length == 0 ? null : [value];
        }

        string[] channels = [.. lines.Skip(key + 1)
            .TakeWhile(line => line.StartsWith(LegacyGitHubWorkflows.DotnetVersionIndent, StringComparison.Ordinal) && line.Trim().Length > 0)
            .Select(line => line.Trim())];
        return channels.Length == 0 ? null : channels;
    }

    [GeneratedRegex(@"lockflight/lockflight/action@[0-9a-f]{40} # v\S+")]
    private static partial Regex ActionPin();
}
