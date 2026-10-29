using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Init;

/// <summary>
/// The CI job of a unit repository of a workspace shell (<c>init --unit-of</c>): before the gates (or the CI judge) it clones the
/// shell read-only with <c>lockflight workspace clone</c>, and the gates judge with the shell's policy for the unit
/// (<c>gate run --shell</c>, <c>ci --shell</c>). The token reaches only the clone step where the platform runs steps apart
/// (GitHub, where the CI judge's action clones in a step of its own, and Azure); in a single-script job (Bitbucket, GitLab) the
/// script unsets it before any of the pull request's code runs.
/// </summary>
internal static class CiShellPointer
{
    private const string GateRun = CiShellScripts.GateCommand;

    private const string JudgeRun = CiShellScripts.JudgeCommand;

    private const string GitHubJudgeInput = "          mode: judge\n";

    private const string GitHubDirectory = "$RUNNER_TEMP/lockflight-shell";

    private const string AzureDirectory = "$AGENT_TEMPDIRECTORY/lockflight-shell";

    private const string ContainerDirectory = "$LOCKFLIGHT_TEMP/lockflight-shell";

    private const string GitHubGatesStep = "      - name: Gates\n";

    private const string AzureEngineStep = "    displayName: Install the pinned engine (outside the repository's global.json)\n";

    private const string ContainerGatesComment = "# Gates.";

    /// <summary>
    /// The file with the clone step and the gates (or the CI judge) reading the clone; a file without either (GitLab's root
    /// include) as it is.
    /// </summary>
    public static CiFile ForUnit(CiFile file, CiPlatform platform)
    {
        bool judge = file.Content.Contains(JudgeRun, StringComparison.Ordinal) || file.Content.Contains(GitHubJudgeInput, StringComparison.Ordinal);
        if (!judge && !file.Content.Contains(GateRun, StringComparison.Ordinal))
        {
            return file;
        }

        string content = platform switch
        {
            CiPlatform.GitHub when judge => GitHubJudge(file.Content),
            CiPlatform.GitHub => GitHub(file.Content),
            CiPlatform.Azure => Azure(file.Content),
            _ => Container(file.Content),
        };
        return file with { Content = content };
    }

    private static string GitHub(string workflow) =>
        Insert(workflow, GitHubGatesStep, before: true, $$$"""
                  # This repository is a unit of a workspace shell (shell: in lockflight.yaml): the gates judge with the shell's policy
                  # too, read from a clone of the shell's branch. A private shell needs a read-only token in the LOCKFLIGHT_SHELL_TOKEN
                  # secret (or a deploy key for an SSH URL); only this step sees it.
                  - name: Clone the workspace shell (read-only)
                    env:
                      {{{ShellClone.TokenVariable}}}: ${{ secrets.{{{ShellClone.TokenVariable}}} }}
                    run: lockflight workspace clone --into "{{{GitHubDirectory}}}"

            """)
            .Replace(GateRun, $"{GateRun} --shell \"{GitHubDirectory}\"", StringComparison.Ordinal);

    private static string GitHubJudge(string workflow) =>
        Insert(workflow, GitHubJudgeInput, before: false, $$$"""
                      # This repository is a unit of a workspace shell (shell: in lockflight.yaml): the action clones the shell's
                      # branch read-only and the judge uses the shell's policy too. A private shell needs a read-only token in the
                      # LOCKFLIGHT_SHELL_TOKEN secret (or a deploy key for an SSH URL); only the action's clone step sees it.
                      shell-clone: "true"
                      shell-token: ${{ secrets.{{{ShellClone.TokenVariable}}} }}

            """);

    private static string Azure(string pipeline) =>
        Insert(pipeline, AzureEngineStep, before: false, $$"""
              # This repository is a unit of a workspace shell (shell: in lockflight.yaml): the gates judge with the shell's policy
              # too, read from a clone of the shell's branch. A private shell needs a read-only token in the secret variable
              # LOCKFLIGHT_SHELL_TOKEN (or a deploy key for an SSH URL); only this step sees it.
              - bash: |
                  set -e
                  lockflight workspace clone --into "{{AzureDirectory}}"
                displayName: Clone the workspace shell (read-only)
                env:
                  {{ShellClone.TokenVariable}}: $({{ShellClone.TokenVariable}})

            """)
            .Replace(GateRun, $"{GateRun} --shell \"{AzureDirectory}\"", StringComparison.Ordinal);

    private static string Container(string job)
    {
        int comment = job.IndexOf(ContainerGatesComment, StringComparison.Ordinal);
        if (comment < 0)
        {
            comment = job.IndexOf(CiShellScripts.JudgeComment, StringComparison.Ordinal);
        }

        if (comment < 0)
        {
            throw new InvalidOperationException($"the CI job has no '{ContainerGatesComment}' or '{CiShellScripts.JudgeComment}' line to put the workspace shell clone before");
        }

        int lineStart = job.LastIndexOf('\n', comment) + 1;
        string indent = job[lineStart..comment];
        string[] lines =
        [
            "# This repository is a unit of a workspace shell (shell: in lockflight.yaml): the gates judge with the shell's policy",
            "# too, read from a clone of the shell's branch. A private shell needs a read-only token in the secured variable",
            "# LOCKFLIGHT_SHELL_TOKEN (or a deploy key for an SSH URL); it is unset before the pull request's code runs.",
            $"lockflight workspace clone --into \"{ContainerDirectory}\"",
            $"unset {ShellClone.TokenVariable}",
        ];
        string clone = string.Concat(lines.Select(line => indent + line + "\n"));
        return (job[..lineStart] + clone + job[lineStart..])
            .Replace(GateRun, $"{GateRun} --shell \"{ContainerDirectory}\"", StringComparison.Ordinal)
            .Replace(JudgeRun, $"lockflight ci --shell \"{ContainerDirectory}\" --platform", StringComparison.Ordinal);
    }

    private static string Insert(string text, string anchor, bool before, string addition)
    {
        int index = text.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"the CI file has no '{anchor.Trim()}' line to put the workspace shell clone next to");
        }

        int at = before ? index : index + anchor.Length;
        return text[..at] + addition + text[at..];
    }
}
