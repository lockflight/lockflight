using Lockflight.Cli.Adoption;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Config.Migration;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Updating;

/// <summary>
/// <c>lockflight update</c>, a person's command: brings Lockflight up to date in one step. An install from source pulls the
/// checkout it was built from and reinstalls the engine and the plugin (<c>scripts/dev-install.sh</c>, or
/// <c>scripts/dev-install.ps1</c> on Windows); an install from a package installs the newest engine from the package feed
/// (<see cref="PackageUpdate"/>). Then the new engine takes over
/// (<c>update --project</c>): it re-pins and links the current project and refreshes the CLAUDE.md block. The tool pin and the
/// block are protected, so the agent cannot run this: updating the engine changes the judge.
/// </summary>
internal sealed class UpdateCommand(CliContext context) : ICommand
{
    private const string SourceOption = "--source";
    private const string DryRunOption = "--dry-run";
    private const string ProjectOption = "--project";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        string[] options = arguments.RestArray;
        string? feed;
        switch (options)
        {
            case [ProjectOption, ..] when ProjectArguments(options[1..]) is { } project:
                return await UpdateProjectAsync(project.DryRun, project.Feed, cancellationToken);
            case []:
                feed = null;
                break;
            case [SourceOption, string value] when value.Length > 0:
                feed = value;
                break;
            default:
                await context.Error.WriteLineAsync($"lockflight: update takes only {SourceOption} <feed> (a package source URL or folder); `update --project {DryRunOption}` shows what the project step would change");
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

        ExitCode installed = source is null
            ? await UpdateFromPackageAsync(home, feed, cancellationToken)
            : await UpdateFromSourceAsync(source, cancellationToken);
        return installed == ExitCode.Pass ? await HandOverAsync(home, feed, cancellationToken) : installed;
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
        (string script, List<string> arguments) = DevInstallScript(source);
        ProcessResult install = await context.Processes(script, arguments, source, cancellationToken);
        await context.Output.WriteAsync(install.Output);
        if (install.ExitCode != 0)
        {
            await context.Error.WriteLineAsync($"lockflight: the install failed: {install.Error.Trim()}");
            return ExitCode.Error;
        }

        return ExitCode.Pass;
    }

    /// <summary>
    /// The checkout's install script, told that update prints the next steps: <c>dev-install.ps1</c> through Windows PowerShell,
    /// which every Windows has, else <c>dev-install.sh</c>.
    /// </summary>
    private static (string FileName, List<string> Arguments) DevInstallScript(string source) => OperatingSystem.IsWindows()
        ? ("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(source, "scripts", "dev-install.ps1"), "-Updating"])
        : ("sh", ["-c", "LOCKFLIGHT_UPDATING=1 exec sh \"$0\"", Path.Combine(source, "scripts", "dev-install.sh")]);

    private async Task<ExitCode> UpdateFromPackageAsync(string home, string? feed, CancellationToken cancellationToken)
    {
        await context.Output.WriteLineAsync($"Installing the newest {PackageUpdate.PackageId} from {feed ?? "the configured NuGet sources"}");
        PackageUpdateResult result = await new PackageUpdate(context.Processes, context.Time, UserInstall.PlatformLayout).RunAsync(home, feed, cancellationToken);
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
    /// <summary>The options after <c>--project</c>: <c>--dry-run</c> and <c>--source &lt;feed&gt;</c>, in any order; null when others are given.</summary>
    private static (bool DryRun, string? Feed)? ProjectArguments(string[] rest) => rest switch
    {
        [] => (false, null),
        [DryRunOption] => (true, null),
        [SourceOption, string feed] when feed.Length > 0 => (false, feed),
        [DryRunOption, SourceOption, string feed] when feed.Length > 0 => (true, feed),
        [SourceOption, string feed, DryRunOption] when feed.Length > 0 => (true, feed),
        _ => null,
    };

    private async Task<ExitCode> HandOverAsync(string home, string? feed, CancellationToken cancellationToken)
    {
        if (ManifestLocator.Discover(context.WorkingDirectory) is not { Manifest: Manifest manifest })
        {
            await context.Output.WriteLineAsync("Not in a Lockflight project: run `lockflight update` in each project to re-pin it.");
            return ExitCode.Pass;
        }

        ProcessResult project = await context.Processes(
            UserInstall.Engine(home), feed is null ? ["update", ProjectOption] : ["update", ProjectOption, SourceOption, feed], manifest.Root, cancellationToken);
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

    /// <param name="dryRun">Print what would change and write nothing.</param>
    /// <param name="feed">Where the release's hashes are read from for the engine lock; null for <c>LOCKFLIGHT_NUGET_FEED</c>, else nuget.org.</param>
    private async Task<ExitCode> UpdateProjectAsync(bool dryRun, string? feed, CancellationToken cancellationToken)
    {
        ManifestLoadResult loaded = ManifestLocator.Discover(context.WorkingDirectory);
        if (loaded.Error is ManifestError invalid)
        {
            await context.Error.WriteLineAsync($"lockflight: {invalid.Describe()}; fix it first, nothing was changed");
            return ExitCode.Config;
        }

        if (loaded is not { Manifest: Manifest manifest })
        {
            await context.Error.WriteLineAsync($"lockflight: no {Manifest.FileName} here or above");
            return ExitCode.Config;
        }

        string root = manifest.Root;
        // Every file is computed first and written last, so an error leaves the project as it was.
        List<PlannedWrite> writes = [];
        string manifestPath = Path.Combine(root, Manifest.FileName);
        string manifestText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(manifestPath, cancellationToken));
        ManifestMigrationResult migration = ManifestMigration.Migrate(manifestText, root, manifestPath);
        switch (migration.Status)
        {
            case ManifestMigrationStatus.Invalid:
                await context.Error.WriteLineAsync($"lockflight: {migration.Message}");
                return ExitCode.Config;
            case ManifestMigrationStatus.Migrated:
                writes.Add(new PlannedWrite(Manifest.FileName, manifestPath, manifestText, migration.Text!));
                break;
            case ManifestMigrationStatus.ManualEdit:
                await context.Error.WriteLineAsync($"lockflight: {migration.Message}. Add this by hand (a person's edit; the file is protected):");
                await context.Error.WriteLineAsync(migration.Snippet);
                break;
        }

        string toolManifest = ToolManifestWriter.RelativePathIn(root);
        string pinPath = Path.Combine(root, toolManifest);
        string? tools = File.Exists(pinPath) ? await File.ReadAllTextAsync(pinPath, cancellationToken) : null;
        if (ToolManifestWriter.Problem(tools) is string problem)
        {
            await context.Error.WriteLineAsync($"lockflight: {toolManifest} {problem}; nothing was updated");
            return ExitCode.Config;
        }

        (string pinned, bool pinChanged) = ToolManifestWriter.Pin(tools);
        if (pinChanged)
        {
            writes.Add(new PlannedWrite(Path.GetRelativePath(root, pinPath).Replace('\\', '/'), pinPath, tools, pinned));
        }

        EngineLockPlan lockPlan = await EngineLockPlanner.PlanAsync(
            root, ToolPin.RunningVersion, context.Engine.Path, feed is null ? EnginePackageFeed.FromEnvironment(context.Environment) : new EnginePackageFeed(feed, context.Environment),
            GitHubReleaseHashes.FetchAsync, context.Processes, cancellationToken);
        await PlanLockAsync(root, lockPlan, writes, cancellationToken);

        string claudeMd = Path.Combine(root, "CLAUDE.md");
        if (File.Exists(claudeMd) && await File.ReadAllTextAsync(claudeMd, cancellationToken) is string text
            && ClaudeMdBlock.IsPresent(text) && !ClaudeMdBlock.IsCurrent(text))
        {
            writes.Add(new PlannedWrite("CLAUDE.md", claudeMd, text, ClaudeMdBlock.Upsert(text)));
        }

        if (File.Exists(Path.Combine(root, Manifest.UnitFileName)))
        {
            await context.Output.WriteLineAsync(
                $"note: this repository holds {Manifest.UnitFileName}, a vendored unit policy. Make it a unit of its shell with `{EngineCommand.Name} init --unit-of <shell url> --unit-path <path>`, then delete {Manifest.UnitFileName}; update does not do it (the file names no shell URL).");
        }

        if (dryRun)
        {
            return await ReportDryRunAsync(writes, migration, cancellationToken);
        }

        await PlannedWrite.ApplyAllAsync(writes, cancellationToken);
        LedgerWriter ledger = new(root, context.Time);
        foreach (PlannedWrite write in writes)
        {
            await ledger.AppendAsync(new LedgerEvent(LedgerEventType.ConfigWritten, $"update:{write.RelativePath}", "updated", ToolPin.RunningVersion), cancellationToken);
        }

        ExitCode link = await new LinkCommand(context with { WorkingDirectory = root }).RunAsync(cancellationToken);
        if (link != ExitCode.Pass)
        {
            return link;
        }

        await ReportNewChecksAsync(migration);
        await context.Output.WriteLineAsync(writes.Count == 0
            ? $"Lockflight {ToolPin.RunningVersion}: this project was already up to date."
            : $"Lockflight {ToolPin.RunningVersion}: updated {string.Join(", ", writes.Select(write => write.RelativePath))}. Review them and commit them in one commit.");
        await MascotDisplay.DrawAsync(context, MascotPose.Pass, $"Up to date: {ToolPin.RunningVersion}");
        return ExitCode.Pass;
    }

    /// <summary>
    /// The lock and, for a committed lock, the vendored install script, when they differ from what is there. Without a lock the
    /// project keeps its version pin, and the output says why.
    /// </summary>
    private async Task PlanLockAsync(string root, EngineLockPlan plan, List<PlannedWrite> writes, CancellationToken cancellationToken)
    {
        // A development build's local lock pins that build for this clone; once a release (or no lock) takes over it is stale.
        string localLock = EngineLockFiles.LocalPath(root);
        if (plan.LockPath != localLock && File.Exists(localLock))
        {
            writes.Add(new PlannedWrite(EngineLock.LocalRelativePath, localLock, await File.ReadAllTextAsync(localLock, cancellationToken), NewText: null));
        }

        if (plan.Lock is not EngineLock engineLock)
        {
            await context.Output.WriteLineAsync($"note: no engine lock written ({plan.Reason}); the project stays pinned by version in dotnet-tools.json");
            return;
        }

        foreach (string note in plan.Notes)
        {
            await context.Output.WriteLineAsync($"engine lock: {note}");
        }

        string lockText = EngineLockText.Write(engineLock);
        string? existingLock = File.Exists(plan.LockPath!) ? await File.ReadAllTextAsync(plan.LockPath!, cancellationToken) : null;
        if (existingLock != lockText)
        {
            writes.Add(new PlannedWrite(Path.GetRelativePath(root, plan.LockPath!).Replace('\\', '/'), plan.LockPath!, existingLock, lockText));
        }

        if (!plan.Committed)
        {
            return;
        }

        string scriptPath = Path.Combine(root, InstallScript.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        string scriptText = Encoding.UTF8.GetString(InstallScript.Bytes);
        string? existingScript = File.Exists(scriptPath) ? Encoding.UTF8.GetString(await File.ReadAllBytesAsync(scriptPath, cancellationToken)) : null;
        if (existingScript != scriptText)
        {
            writes.Add(new PlannedWrite(InstallScript.RelativePath, scriptPath, existingScript, scriptText));
        }
    }

    private async Task<ExitCode> ReportDryRunAsync(List<PlannedWrite> writes, ManifestMigrationResult migration, CancellationToken cancellationToken)
    {
        await context.Output.WriteLineAsync(writes.Count == 0
            ? $"update --dry-run: nothing to change (Lockflight {ToolPin.RunningVersion})."
            : $"update --dry-run: nothing was written. Lockflight {ToolPin.RunningVersion} would change:");
        foreach (PlannedWrite write in writes)
        {
            await context.Output.WriteLineAsync($"--- {write.RelativePath}");
            await context.Output.WriteLineAsync(await write.DiffAsync(cancellationToken));
        }

        await ReportNewChecksAsync(migration);
        return ExitCode.Pass;
    }

    private async Task ReportNewChecksAsync(ManifestMigrationResult migration)
    {
        if (migration.NewChecks.Count == 0)
        {
            return;
        }

        await context.Output.WriteLineAsync($"{Manifest.FileName} is now version 2. New in it, set so that nothing judges differently:");
        foreach (string check in migration.NewChecks)
        {
            await context.Output.WriteLineAsync($"  {check}");
        }
    }
}
