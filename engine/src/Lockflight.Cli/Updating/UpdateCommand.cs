using Lockflight.Cli.Adoption;
using System.Text;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Config.Migration;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Projects;

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
    private const string JudgeWorkflowOption = "--judge-workflow";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        string[] options = arguments.RestArray;
        string? feed;
        switch (options)
        {
            case [ProjectOption, ..] when ProjectArguments(options[1..]) is { } project:
                return await UpdateProjectAsync(project.DryRun, project.Feed, project.JudgeWorkflow, cancellationToken);
            case []:
                feed = null;
                break;
            case [SourceOption, string value] when value.Length > 0:
                feed = value;
                break;
            default:
                await context.Error.WriteLineAsync($"lockflight: update takes only {SourceOption} <feed> (a package source URL or folder); `update --project {DryRunOption}` shows what the project step would change, and `update --project {JudgeWorkflowOption}` replaces a gates-only GitHub workflow with the CI judge's");
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

    /// <summary>
    /// The options after <c>--project</c>, in any order: <c>--dry-run</c>, <c>--source &lt;feed&gt;</c> and <c>--judge-workflow</c>; null
    /// when another is given or one is given twice.
    /// </summary>
    private static (bool DryRun, string? Feed, bool JudgeWorkflow)? ProjectArguments(string[] rest)
    {
        bool dryRun = false;
        bool judgeWorkflow = false;
        string? feed = null;
        for (int index = 0; index < rest.Length; index++)
        {
            switch (rest[index])
            {
                case DryRunOption when !dryRun:
                    dryRun = true;
                    break;
                case JudgeWorkflowOption when !judgeWorkflow:
                    judgeWorkflow = true;
                    break;
                case SourceOption when feed is null && index + 1 < rest.Length && rest[index + 1].Length > 0 && !rest[index + 1].StartsWith("--", StringComparison.Ordinal):
                    feed = rest[++index];
                    break;
                default:
                    return null;
            }
        }

        return (dryRun, feed, judgeWorkflow);
    }

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
    /// <param name="judgeWorkflow">Replace a gates-only GitHub workflow Lockflight wrote with the CI judge's (<see cref="JudgeWorkflowMigration"/>).</param>
    private async Task<ExitCode> UpdateProjectAsync(bool dryRun, string? feed, bool judgeWorkflow, CancellationToken cancellationToken)
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

        // The CI judge's workflow runs the engine the target branch pins, so it cannot arrive with the pin that brings that engine.
        bool engineMoves = pinChanged || (lockPlan.Committed && writes.Any(write => write.Path == lockPlan.LockPath));
        JudgeWorkflowUpdate workflowUpdate = await PlanJudgeWorkflowAsync(root, writes, judgeWorkflow, engineMoves, dryRun, lockPlan.Committed, cancellationToken);
        if (workflowUpdate == JudgeWorkflowUpdate.Refused)
        {
            return ExitCode.Config;
        }

        if (lockPlan.Committed)
        {
            await PlanCiFilesAsync(root, writes, dryRun, skipWorkflow: workflowUpdate == JudgeWorkflowUpdate.Planned, cancellationToken);
        }

        await PlanJudgeRunnerAsync(root, writes, dryRun, cancellationToken);

        string claudeMd = Path.Combine(root, "CLAUDE.md");
        if (File.Exists(claudeMd) && await File.ReadAllTextAsync(claudeMd, cancellationToken) is string text
            && ClaudeMdBlock.IsPresent(text) && !ClaudeMdBlock.IsCurrent(text))
        {
            writes.Add(new PlannedWrite("CLAUDE.md", claudeMd, text, ClaudeMdBlock.Upsert(text)));
        }

        // The native level's vendored hook shim follows the engine, as the plugin's shim follows the plugin.
        string shim = Path.Combine(root, NativeHookShim.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(shim) && await File.ReadAllTextAsync(shim, cancellationToken) is string shimText && shimText != NativeHookShim.Text)
        {
            writes.Add(new PlannedWrite(NativeHookShim.RelativePath, shim, shimText, NativeHookShim.Text));
        }

        // And the native hooks in the settings follow the engine too, when the project has them and they can be merged into.
        string settingsPath = Path.Combine(root, ClaudeSettingsMerger.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(settingsPath) && await File.ReadAllTextAsync(settingsPath, cancellationToken) is string settingsText
            && ClaudeSettingsMerger.Problem(settingsText, nativeHooks: true) is null
            && JsonNode.Parse(settingsText) is JsonObject settings && NativeHooks.AnyPresent(settings) && NativeHooks.Merge(settings))
        {
            writes.Add(new PlannedWrite(ClaudeSettingsMerger.RelativePath, settingsPath, settingsText, JsonFiles.Format(settings)));
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

        string attributesPath = Path.Combine(root, ".gitattributes");
        string? attributes = File.Exists(attributesPath) ? await File.ReadAllTextAsync(attributesPath, cancellationToken) : null;
        if (GitAttributesUpdater.UpdateInstallScript(attributes) is (string withRule, true))
        {
            writes.Add(new PlannedWrite(".gitattributes", attributesPath, attributes, withRule));
        }
    }

    /// <summary>
    /// Each generated CI file with its engine install step and policy restore moved to the lock. A file where those lines cannot
    /// be found exactly as Lockflight wrote them is left alone, and the new step is written to
    /// <c>.lockflight/local/migration/&lt;path&gt;</c> for a person to merge.
    /// </summary>
    private async Task PlanCiFilesAsync(string root, List<PlannedWrite> writes, bool dryRun, bool skipWorkflow, CancellationToken cancellationToken)
    {
        foreach (string relative in CiStepMigration.KnownFiles.Where(file => !skipWorkflow || file != Templates.WorkflowPath))
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            string text = await File.ReadAllTextAsync(path, cancellationToken);
            if (CiStepMigration.Migrate(text) is string migrated)
            {
                if (migrated != text)
                {
                    writes.Add(new PlannedWrite(relative, path, text, migrated));
                }

                continue;
            }

            string fallback = Path.Combine(root, ".lockflight", "local", "migration", relative.Replace('/', Path.DirectorySeparatorChar));
            if (dryRun)
            {
                await context.Output.WriteLineAsync($"note: {relative} does not hold the engine install step or the pull request's base as Lockflight wrote it; update would leave it alone and write the new step for you to merge");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
            await File.WriteAllTextAsync(fallback, $"# Replace the engine install step of {relative} with this, and add .lockflight/engine.lock and .lockflight/ci to its policy restore; on Azure and GitLab take a pull request's base from the target branch's tip, as lockflight init writes it:\n{CiShellScripts.InstallEngine}\n", cancellationToken);
            await context.Output.WriteLineAsync($"note: {relative} does not hold the engine install step or the pull request's base as Lockflight wrote it, so it was left alone; merge {Path.GetRelativePath(root, fallback).Replace('\\', '/')} into it by hand");
        }
    }

    /// <summary>
    /// The GitHub workflow's move to the CI judge's. A gates-only workflow Lockflight wrote is replaced with <paramref name="replace"/>
    /// (refused while <paramref name="engineMoves"/>: the new workflow runs the engine the target branch pins, so the pin is merged
    /// first); without it, the option is named, and under <paramref name="dryRun"/> the diff shown. A gates-only workflow with a
    /// person's edits is refused with <paramref name="replace"/>, noted without. A 0.3.0 judge workflow is renamed here when no
    /// committed lock lets the CI step migration do it.
    /// </summary>
    private async Task<JudgeWorkflowUpdate> PlanJudgeWorkflowAsync(
        string root, List<PlannedWrite> writes, bool replace, bool engineMoves, bool dryRun, bool lockCommitted, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return JudgeWorkflowUpdate.Unchanged;
        }

        string text = await File.ReadAllTextAsync(path, cancellationToken);
        JudgeWorkflowPlan plan = JudgeWorkflowMigration.Plan(text, EngineVersion.IsDevelopmentBuild(ToolPin.RunningVersion));
        string command = $"lockflight update --project {JudgeWorkflowOption}";
        switch (plan.Status)
        {
            case JudgeWorkflowStatus.Recognised when replace && engineMoves:
                await context.Error.WriteLineAsync(
                    $"lockflight: this update also moves the engine pin, and the CI judge's workflow runs the engine the target branch pins, so it cannot come in the same pull request; nothing was changed. Run `lockflight update --project`, merge that, then run `{command}` in a second pull request");
                return JudgeWorkflowUpdate.Refused;
            case JudgeWorkflowStatus.Recognised when replace:
                writes.Add(new PlannedWrite(Templates.WorkflowPath, path, text, plan.Replacement));
                return JudgeWorkflowUpdate.Planned;
            case JudgeWorkflowStatus.Recognised:
                await context.Output.WriteLineAsync(
                    $"note: {Templates.WorkflowPath} runs the gates only. The CI judge's workflow also re-runs each red commit, green at head and trace, and sets the lockflight/judge status next to gates (whose name stays). "
                    + $"To move to it, run `{command}` in a branch{(engineMoves ? " once this update's engine pin is merged" : string.Empty)} and merge it with a person's review{(dryRun ? "; it would change the file so (not part of this update):" : "; `--dry-run` with it shows the diff.")}");
                if (dryRun)
                {
                    await context.Output.WriteLineAsync(await new PlannedWrite(Templates.WorkflowPath, path, text, plan.Replacement).DiffAsync(cancellationToken));
                }

                return JudgeWorkflowUpdate.Unchanged;
            case JudgeWorkflowStatus.Edited when replace:
                await context.Error.WriteLineAsync(
                    $"lockflight: {Templates.WorkflowPath} runs the gates only but was edited outside the lines Lockflight wrote, so update does not replace it; nothing was changed. Move your edits by hand: in a branch, delete it, run `lockflight init` to write the CI judge's workflow, and add them back");
                return JudgeWorkflowUpdate.Refused;
            case JudgeWorkflowStatus.Edited:
                await context.Output.WriteLineAsync(
                    $"note: {Templates.WorkflowPath} runs the gates only and was edited outside the lines Lockflight wrote; to move to the CI judge's workflow, delete it in a branch, run `lockflight init`, and add your edits back");
                return JudgeWorkflowUpdate.Unchanged;
            default:
                if (!lockCommitted && CiStepMigration.Migrate(text) is string renamed && renamed != text && text.Contains("mode: judge", StringComparison.Ordinal))
                {
                    writes.Add(new PlannedWrite(Templates.WorkflowPath, path, text, renamed));
                    return JudgeWorkflowUpdate.Planned;
                }

                return JudgeWorkflowUpdate.Unchanged;
        }
    }

    /// <summary>
    /// The GitHub judge workflow's building jobs moved to the runner the test projects need (<see cref="JudgeRunner"/>): Windows
    /// when one needs Windows, Linux otherwise, on top of any change already planned for the file, and shown as a diff before
    /// anything is written. A job on a label Lockflight does not write is the person's choice and is only noted.
    /// </summary>
    private async Task PlanJudgeRunnerAsync(string root, List<PlannedWrite> writes, bool dryRun, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        int planned = writes.FindIndex(write => write.RelativePath == Templates.WorkflowPath);
        string? current = planned >= 0 ? writes[planned].NewText : File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
        if (current is null || !current.Contains("mode: judge", StringComparison.Ordinal))
        {
            return;
        }

        IReadOnlyList<string> windowsTests = ProjectFiles.WindowsTestProjects(root);
        string moved = JudgeRunner.Apply(current, windows: windowsTests.Count > 0);
        if (moved == current)
        {
            if (windowsTests.Count > 0 && JudgeRunner.RunsOn(current, "judge") is string label && !JudgeRunner.IsWindows(label))
            {
                await context.Output.WriteLineAsync(
                    $"note: a test project needs Windows, and the judge job of {Templates.WorkflowPath} runs on '{label}', a runner chosen by hand, so update leaves it; on a runner that is not Windows the judge reports NEEDS_WINDOWS_RUNNER");
            }

            return;
        }

        PlannedWrite runnerWrite = planned >= 0 ? writes[planned] with { NewText = moved } : new PlannedWrite(Templates.WorkflowPath, path, current, moved);
        if (planned >= 0)
        {
            writes[planned] = runnerWrite;
        }
        else
        {
            writes.Add(runnerWrite);
        }

        string why = windowsTests.Count > 0
            ? $"{string.Join(", ", windowsTests.Select(project => Path.GetRelativePath(root, project).Replace('\\', '/')))} {(windowsTests.Count == 1 ? "targets" : "target")} Windows, so the judge and review jobs move to {JudgeRunner.WindowsLabel} (the report jobs stay on Linux; on private repositories Windows minutes cost twice Linux minutes)"
            : $"no test project needs Windows any more, so the judge and review jobs move back to {JudgeRunner.LinuxLabel}";
        await context.Output.WriteLineAsync($"CI runner: {why}. A runs-on other than {JudgeRunner.LinuxLabel} or {JudgeRunner.WindowsLabel} is left as it is.");
        if (!dryRun)
        {
            await context.Output.WriteLineAsync(await new PlannedWrite(Templates.WorkflowPath, path, current, moved).DiffAsync(cancellationToken));
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
