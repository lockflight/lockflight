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
/// <c>lockflight update</c>, a person's command: brings Lockflight up to date in one step. An install from source pulls the
/// checkout it was built from and reinstalls the engine and the plugin (<c>scripts/dev-install.sh</c>); an install from a package
/// installs the newest engine from the package feed (<see cref="PackageUpdate"/>). Then the new engine takes over
/// (<c>update --project</c>): it re-pins and links the current project and refreshes the CLAUDE.md block. The tool pin and the
/// block are protected, so the agent cannot run this: updating the engine changes the judge.
/// </summary>
internal sealed class UpdateCommand(CliContext context)
{
    private const string SourceOption = "--source";

    public async Task<ExitCode> RunAsync(string[] options, CancellationToken cancellationToken)
    {
        string? feed;
        switch (options)
        {
            case ["--project"]:
                return await UpdateProjectAsync(cancellationToken);
            case []:
                feed = null;
                break;
            case [SourceOption, string value] when value.Length > 0:
                feed = value;
                break;
            default:
                await context.Error.WriteLineAsync($"lockflight: update takes only {SourceOption} <feed> (a package source URL or folder)");
                return ExitCode.Config;
        }

        if (context.Environment.HomeDirectory() is not string home)
        {
            await context.Error.WriteLineAsync("lockflight: update needs the home directory (HOME) to find the user install in ~/.lockflight");
            return ExitCode.Config;
        }

        string? source = UserInstall.Source(home);
        if (source is not null && feed is not null)
        {
            await context.Error.WriteLineAsync($"lockflight: this engine was installed from the checkout {source}, so update pulls it and takes no {SourceOption}. To install from packages instead, delete ~/.lockflight/source and run update again");
            return ExitCode.Config;
        }

        if (OperatingSystem.IsWindows())
        {
            await context.Error.WriteLineAsync(source is null
                ? $"lockflight: on Windows, update the engine with: dotnet tool update {PackageUpdate.PackageId} --prerelease --tool-path %USERPROFILE%\\.lockflight\\bin (run it in your user folder), then %USERPROFILE%\\.lockflight\\bin\\lockflight.exe update --project in each project"
                : $"lockflight: on Windows, update from Git Bash: cd \"{source}\" && git pull && scripts/dev-install.sh");
            return ExitCode.Config;
        }

        ExitCode installed = source is null
            ? await UpdateFromPackageAsync(home, feed, cancellationToken)
            : await UpdateFromSourceAsync(source, cancellationToken);
        return installed == ExitCode.Pass ? await HandOverAsync(home, cancellationToken) : installed;
    }

    private async Task<ExitCode> UpdateFromSourceAsync(string source, CancellationToken cancellationToken)
    {
        await context.Output.WriteLineAsync($"Pulling {source}");
        ProcessResult pull = await context.Processes("git", ["-C", source, "pull", "--ff-only", "--quiet"], source, cancellationToken);
        if (pull.ExitCode != 0)
        {
            await context.Error.WriteLineAsync($"lockflight: git pull failed in {source}: {pull.Error.Trim()}");
            return ExitCode.Error;
        }

        await context.Output.WriteLineAsync("Installing the engine and the plugin");
        ProcessResult install = await context.Processes(
            "sh", ["-c", "LOCKFLIGHT_UPDATING=1 exec sh \"$0\"", Path.Combine(source, "scripts", "dev-install.sh")], source, cancellationToken);
        await context.Output.WriteAsync(install.Output);
        if (install.ExitCode != 0)
        {
            await context.Error.WriteLineAsync($"lockflight: the install failed: {install.Error.Trim()}");
            return ExitCode.Error;
        }

        return ExitCode.Pass;
    }

    private async Task<ExitCode> UpdateFromPackageAsync(string home, string? feed, CancellationToken cancellationToken)
    {
        await context.Output.WriteLineAsync($"Installing the newest {PackageUpdate.PackageId} from {feed ?? "the configured NuGet sources"}");
        PackageUpdateResult result = await new PackageUpdate(context.Processes, context.Time).RunAsync(home, feed, cancellationToken);
        switch (result.Status)
        {
            case PackageUpdateStatus.Installed:
                await context.Output.WriteLineAsync($"Installed Lockflight {result.Version}: {EngineCommand.UserInstall}");
                break;
            case PackageUpdateStatus.UpToDate:
                await context.Output.WriteLineAsync($"Lockflight {result.Version} is already the newest on the feed.");
                break;
            case PackageUpdateStatus.KeptNewer:
                await context.Output.WriteLineAsync($"The installed Lockflight {result.Version} is newer than the feed's; kept it.");
                break;
            default:
                await context.Error.WriteLineAsync($"lockflight: the update failed: {result.Problem}. Check the feed ({feed ?? "the NuGet configuration"}) and the network, then run update again");
                return ExitCode.Error;
        }

        await context.Output.WriteLineAsync("The plugin updates from its marketplace; to update it now: claude plugin update lockflight@lockflight");
        return ExitCode.Pass;
    }

    /// <summary>The new engine updates the project, so the pin names its own version and the block is its own text.</summary>
    private async Task<ExitCode> HandOverAsync(string home, CancellationToken cancellationToken)
    {
        if (ManifestLocator.Discover(context.WorkingDirectory) is not { Manifest: Manifest manifest })
        {
            await context.Output.WriteLineAsync("Not in a Lockflight project: run `lockflight update` in each project to re-pin it.");
            return ExitCode.Pass;
        }

        ProcessResult project = await context.Processes(UserInstall.Engine(home), ["update", "--project"], manifest.Root, cancellationToken);
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
