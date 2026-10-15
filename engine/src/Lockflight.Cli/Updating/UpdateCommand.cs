using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Updating;

/// <summary>
/// <c>lockflight update</c>, a person's command: brings Lockflight up to date in one step. It pulls the checkout the engine was
/// installed from, rebuilds and reinstalls the engine and the plugin (<c>scripts/dev-install.sh</c>), then hands over to the new
/// engine (<c>update --project</c>), which re-pins and links the current project and refreshes the CLAUDE.md block. The tool pin
/// and the block are protected, so the agent cannot run this: updating the engine changes the judge.
/// </summary>
internal sealed class UpdateCommand(CliContext context)
{
    public async Task<ExitCode> RunAsync(string[] options, CancellationToken cancellationToken)
    {
        if (options is ["--project"])
        {
            return await UpdateProjectAsync(cancellationToken);
        }

        if (options.Length > 0)
        {
            await context.Error.WriteLineAsync("lockflight: update takes no options");
            return ExitCode.Config;
        }

        string? home = context.Environment.HomeDirectory();
        string? source = home is null ? null : UserInstall.Source(home);
        if (home is null || source is null)
        {
            await context.Error.WriteLineAsync("lockflight: update works for an install from source (scripts/dev-install.sh), and none was found; with a tool pin, run `dotnet tool update Lockflight.Cli` instead");
            return ExitCode.Config;
        }

        if (OperatingSystem.IsWindows())
        {
            await context.Error.WriteLineAsync($"lockflight: on Windows, update from Git Bash: cd \"{source}\" && git pull && scripts/dev-install.sh");
            return ExitCode.Config;
        }

        await context.Output.WriteLineAsync($"Pulling {source}");
        ProcessResult pull = await ProcessRunner.RunAsync("git", ["-C", source, "pull", "--ff-only", "--quiet"], source, cancellationToken);
        if (pull.ExitCode != 0)
        {
            await context.Error.WriteLineAsync($"lockflight: git pull failed in {source}: {pull.Error.Trim()}");
            return ExitCode.Error;
        }

        await context.Output.WriteLineAsync("Installing the engine and the plugin");
        ProcessResult install = await ProcessRunner.RunAsync(
            "sh", ["-c", "LOCKFLIGHT_UPDATING=1 exec sh \"$0\"", Path.Combine(source, "scripts", "dev-install.sh")], source, cancellationToken);
        await context.Output.WriteAsync(install.Output);
        if (install.ExitCode != 0)
        {
            await context.Error.WriteLineAsync($"lockflight: the install failed: {install.Error.Trim()}");
            return ExitCode.Error;
        }

        if (ManifestLocator.Discover(context.WorkingDirectory) is not { Manifest: Manifest manifest })
        {
            await context.Output.WriteLineAsync("Not in a Lockflight project: run `lockflight update` in each project to re-pin it.");
            return ExitCode.Pass;
        }

        // The new engine updates the project, so the pin names its own version and the block is its own text.
        ProcessResult project = await ProcessRunner.RunAsync(UserInstall.Engine(home), ["update", "--project"], manifest.Root, cancellationToken);
        await context.Output.WriteAsync(project.Output);
        await context.Error.WriteAsync(project.Error);
        if (project.ExitCode != 0)
        {
            return (ExitCode)project.ExitCode;
        }

        // The new engine's output came through a pipe, so Levi is drawn here, at the person's terminal.
        await MascotDisplay.DrawAsync(context, MascotPose.Pass, "Lockflight is up to date.");
        return ExitCode.Pass;
    }

    private async Task<ExitCode> UpdateProjectAsync(CancellationToken cancellationToken)
    {
        if (ManifestLocator.Discover(context.WorkingDirectory) is not { Manifest: Manifest manifest })
        {
            await context.Error.WriteLineAsync($"lockflight: no {Manifest.FileName} here or above");
            return ExitCode.Config;
        }

        string root = manifest.Root;
        List<string> changed = [];
        string pinPath = Path.Combine(root, ToolManifestWriter.RelativePathIn(root));
        (string pinned, bool pinChanged) = ToolManifestWriter.Pin(File.Exists(pinPath) ? await File.ReadAllTextAsync(pinPath, cancellationToken) : null);
        if (pinChanged)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pinPath)!);
            await File.WriteAllTextAsync(pinPath, pinned, cancellationToken);
            changed.Add(Path.GetRelativePath(root, pinPath).Replace('\\', '/'));
        }

        string claudeMd = Path.Combine(root, "CLAUDE.md");
        if (File.Exists(claudeMd) && await File.ReadAllTextAsync(claudeMd, cancellationToken) is string text
            && ClaudeMdBlock.IsPresent(text) && !ClaudeMdBlock.IsCurrent(text))
        {
            await File.WriteAllTextAsync(claudeMd, ClaudeMdBlock.Upsert(text), cancellationToken);
            changed.Add("CLAUDE.md");
        }

        LedgerWriter ledger = new(root, context.Time);
        foreach (string file in changed)
        {
            await ledger.AppendAsync(new LedgerEvent(LedgerEventType.ConfigWritten, $"update:{file}", "updated", ToolPin.RunningVersion), cancellationToken);
        }

        ExitCode link = await new LinkCommand(context with { WorkingDirectory = root }).RunAsync(cancellationToken);
        if (link != ExitCode.Pass)
        {
            return link;
        }

        await context.Output.WriteLineAsync(changed.Count == 0
            ? $"Lockflight {ToolPin.RunningVersion}: this project was already up to date."
            : $"Lockflight {ToolPin.RunningVersion}: updated {string.Join(", ", changed)}. Review and commit them.");
        await MascotDisplay.DrawAsync(context, MascotPose.Pass, $"Up to date: {ToolPin.RunningVersion}");
        return ExitCode.Pass;
    }
}
