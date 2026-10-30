using Lockflight.Cli.Config;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The workspace shell a unit repository's pull request is judged with. The pointer (<c>shell:</c>) is read from the target
/// branch's <c>lockflight.yaml</c>, never from the pull request, and the clone is the one <c>--shell</c> names (in CI, made by
/// <c>lockflight workspace clone</c>; it must be a clone of the shell the pointer names) or, locally, the shell the unit is
/// checked out in (its checked-out commit, which may not be merged). A unit whose shell cannot be read is not judged without it; a
/// <c>--shell</c> given while the target branch names no shell is ignored.
/// </summary>
internal static class JudgeShell
{
    public static async Task<ShellPolicyReading> ReadAsync(JudgePlan plan, CancellationToken cancellationToken)
    {
        JudgeInputs inputs = plan.Inputs;
        if (plan.Policy.Manifest?.Shell is not ShellSettings pointer)
        {
            // The pull request that adds the pointer is judged by a target branch without it, as the repository it still is (the
            // template's clone step then cloned nothing), exactly as gate run ignores --shell there.
            return new ShellPolicyReading(Policy: null, Error: null);
        }

        string? directory = inputs.ShellPath ?? (inputs.Platform == JudgePlatform.Local ? ShellLocator.FindEnclosing(inputs.Root, pointer) : null);
        if (directory is null)
        {
            return new ShellPolicyReading(Policy: null,
                $"this repository is a unit of the workspace shell {pointer.Url}, whose policy and specs judge its pull requests; clone the shell with `lockflight workspace clone --into <dir>` (outside the repository) and pass {JudgeInputsReader.ShellOption} <dir>");
        }

        return await ShellPolicyReader.ReadAsync(directory, pointer, checkOrigin: inputs.ShellPath is not null, cancellationToken);
    }
}
