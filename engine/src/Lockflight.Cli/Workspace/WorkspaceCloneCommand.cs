using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// <c>lockflight workspace clone --into &lt;dir&gt;</c>: in a unit repository (<c>shell:</c> in <c>lockflight.yaml</c>), clones the
/// workspace shell it points at into a directory outside the repository and checks that the shell's policy lists this unit. CI runs
/// it after restoring the base branch's manifest, then passes the clone to <c>gate run --shell</c>, so the unit is judged by the
/// shell's policy without a vendored copy. The clone needs only read access: a read-only token in
/// <see cref="ShellClone.TokenVariable"/> or a deploy key.
/// </summary>
internal sealed class WorkspaceCloneCommand(CliContext context) : ICommand
{
    public const string IntoOption = "--into";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options
            || await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        if (options.Value(IntoOption) is not string into)
        {
            await context.Error.WriteLineAsync($"lockflight workspace clone: {IntoOption} <dir> is required: a directory outside the repository for the shell's clone");
            return ExitCode.Config;
        }

        // The policy decides: CI runs this after restoring the base branch's manifest, and a base without the pointer (the pull
        // request that adds it) is judged as the repository it still is.
        if (manifest.Shell is not ShellSettings pointer)
        {
            await context.Output.WriteLineAsync($"Not a unit of a workspace shell ({Manifest.FileName} has no shell: section); nothing to clone.");
            return ExitCode.Pass;
        }

        string directory = Path.GetFullPath(into, context.WorkingDirectory);
        if (Inside(directory, manifest.Root))
        {
            await context.Error.WriteLineAsync($"lockflight workspace clone: '{directory}' is inside the repository; clone the shell outside it (for example under the CI job's temporary directory)");
            return ExitCode.Config;
        }

        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            await context.Error.WriteLineAsync($"lockflight workspace clone: '{directory}' is not empty; give a new or empty directory");
            return ExitCode.Config;
        }

        ShellCloneResult clone = await ShellClone.CloneAsync(pointer.Url, pointer.Ref, directory, context.Environment, cancellationToken);
        if (!clone.Succeeded)
        {
            await context.Error.WriteLineAsync($"lockflight workspace clone: {clone.Error}");
            return ExitCode.Error;
        }

        ShellPolicyReading reading = await ShellPolicyReader.ReadAsync(directory, pointer, checkOrigin: true, cancellationToken);
        if (reading.Policy is not ShellPolicy policy)
        {
            await context.Error.WriteLineAsync($"lockflight workspace clone: {reading.Error}");
            return ExitCode.Error;
        }

        await context.Output.WriteLineAsync($"Cloned the workspace shell {pointer.Url} at {CommitIds.Short(policy.Commit)} into {directory}; it lists this repository as unit {policy.Unit.Path}.");
        await context.Output.WriteLineAsync($"Judge with its policy: lockflight gate run --profile pr --shell \"{directory}\" …");
        return ExitCode.Pass;
    }

    private static bool Inside(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), path);
        bool outside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative);
        return !outside;
    }
}
