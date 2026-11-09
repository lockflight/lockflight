using System.Text;
using System.Text.Json.Nodes;
using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Config.Migration;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Gates.WindowsTargets;
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
    private const string JudgeWorkflowOption = "--judge-workflow";

    /// <summary>The CI files <c>init</c> writes for the platforms other than GitHub, which <c>update</c> does not migrate.</summary>
    private static readonly (string Path, string Platform)[] OtherCiFiles =
    [
        (AzurePipelinesTemplate.Path, "Azure Pipelines"),
        (BitbucketPipelinesTemplate.Path, "Bitbucket Pipelines"),
        (BitbucketPipelinesTemplate.FragmentPath, "Bitbucket Pipelines"),
        (GitLabCiTemplate.Path, "GitLab CI/CD"),
    ];

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
        string? projectRoot = VendoredUnit.Find(context.WorkingDirectory) ?? ManifestLocator.DiscoverForMigration(context.WorkingDirectory).Manifest?.Root;
        if (projectRoot is null)
        {
            await context.Output.WriteLineAsync("Not in a Lockflight project: run `lockflight update` in each project to re-pin it.");
            return ExitCode.Pass;
        }

        ProcessResult project = await context.Processes(
            UserInstall.Engine(home), feed is null ? ["update", ProjectOption] : ["update", ProjectOption, SourceOption, feed], projectRoot, cancellationToken);
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
        if (VendoredUnit.Find(context.WorkingDirectory) is string unitRoot)
        {
            return await MoveVendoredUnitAsync(unitRoot, dryRun, cancellationToken);
        }

        ManifestLoadResult loaded = ManifestLocator.DiscoverForMigration(context.WorkingDirectory);
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
                // The manifest stays version 1, which nothing but this migration reads: the rest of the update waits for the edit.
                await context.Error.WriteLineAsync($"lockflight: {migration.Message}. Add this by hand (a person's edit; the file is protected), then run update --project again; nothing was changed:");
                await context.Error.WriteLineAsync(migration.Snippet);
                return ExitCode.Config;
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

        string gitIgnorePath = Path.Combine(root, ".gitignore");
        string? gitIgnore = File.Exists(gitIgnorePath) ? await File.ReadAllTextAsync(gitIgnorePath, cancellationToken) : null;
        if (GitIgnoreUpdater.Update(gitIgnore) is (string ignored, true))
        {
            writes.Add(new PlannedWrite(".gitignore", gitIgnorePath, gitIgnore, ignored));
        }

        await NoteOtherCiPlatformsAsync(root, cancellationToken);

        // Last, so the deny rules and the sandbox write denials cover the protected files this update is about to write.
        await PlanClaudeSettingsAsync(root, manifest, writes, cancellationToken);

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
    /// Moves a unit repository from the policy the removed <c>workspace sync</c> vendored into it to the shell pointer: the shell it
    /// is checked out in names its path and, through its <c>origin</c>, its URL; the vendored file and the unit workflow sync wrote
    /// are deleted, and <c>init --unit-of</c> writes the unit's own <c>lockflight.yaml</c>, its pin and the CI judge's workflow that
    /// clones the shell; then the engine is linked, as after any update. If init refuses (it writes nothing then), the deleted files
    /// are put back.
    /// </summary>
    private async Task<ExitCode> MoveVendoredUnitAsync(string unitRoot, bool dryRun, CancellationToken cancellationToken)
    {
        string fallback = $"`{EngineCommand.Name} init --unit-of <shell url> --unit-path <path>` there, then delete {Manifest.UnitFileName}";
        if (VendoredUnit.EnclosingShell(unitRoot) is not (string shell, string unitPath))
        {
            await context.Error.WriteLineAsync(
                $"lockflight: {unitRoot} holds {Manifest.UnitFileName}, the policy the removed workspace sync vendored, which this engine no longer reads. No {Manifest.FileName} above it lists this repository among its units, so update cannot tell which shell it belongs to: check it out inside its shell and run update there, or run {fallback}; nothing was changed");
            return ExitCode.Config;
        }

        ProcessResult origin = await context.Processes("git", ["-C", shell, "remote", "get-url", "origin"], shell, cancellationToken);
        string url = origin.Output.Trim();
        if (origin.ExitCode != 0 || url.Length == 0)
        {
            await context.Error.WriteLineAsync(
                $"lockflight: the workspace shell at {shell} has no origin remote, so update cannot tell the unit where to clone it from: run {fallback}; nothing was changed");
            return ExitCode.Config;
        }

        string workflowPath = Path.Combine(unitRoot, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        string? workflow = File.Exists(workflowPath) ? await File.ReadAllTextAsync(workflowPath, cancellationToken) : null;
        List<(string Path, string Text)> removed = [(Path.Combine(unitRoot, Manifest.UnitFileName), await File.ReadAllTextAsync(Path.Combine(unitRoot, Manifest.UnitFileName), cancellationToken))];
        if (workflow is not null && JudgeWorkflowMigration.IsVendoredUnitWorkflow(workflow))
        {
            removed.Add((workflowPath, workflow));
        }

        string[] init = ["--unit-of", url, "--unit-path", unitPath];
        string deletes = string.Join(" and ", removed.Select(file => Path.GetRelativePath(unitRoot, file.Path).Replace('\\', '/')));
        await context.Output.WriteLineAsync(
            $"{Manifest.UnitFileName} is the policy the removed workspace sync vendored. Moving this repository to the shell pointer (shell {url}, unit {unitPath}): {(dryRun ? "update would delete" : "deleting")} {deletes}, then {(dryRun ? "run" : "running")} init {string.Join(' ', init)}.");
        if (dryRun)
        {
            await context.Output.WriteLineAsync("update --dry-run: nothing was written; run init with --dry-run after deleting those files to see the rest.");
            return ExitCode.Pass;
        }

        foreach ((string path, _) in removed)
        {
            File.Delete(path);
        }

        CommandDescriptor initCommand = CommandRegistry.Match(["init"])!;
        ExitCode initialized = await new InitCommand(context with { WorkingDirectory = unitRoot }).RunAsync(new CommandArguments(initCommand, [.. init, "--no-link"]), cancellationToken);
        if (initialized != ExitCode.Pass)
        {
            foreach ((string path, string text) in removed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, text, cancellationToken);
            }

            await context.Error.WriteLineAsync($"lockflight: init did not finish, so {deletes} {(removed.Count == 1 ? "was" : "were")} put back");
            return initialized;
        }

        await context.Output.WriteLineAsync($"Moved to the shell pointer: {deletes} deleted. Review the changes and commit them in one commit.");
        return await new LinkCommand(context with { WorkingDirectory = unitRoot }).RunAsync(cancellationToken);
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
                switch (await PlanWorkflowRefreshAsync(root, path, text, writes, dryRun, cancellationToken))
                {
                    case JudgeWorkflowRefreshStatus.Outdated:
                        return JudgeWorkflowUpdate.Planned;
                    case JudgeWorkflowRefreshStatus.Current:
                        return JudgeWorkflowUpdate.Unchanged;
                }

                if (!lockCommitted && CiStepMigration.Migrate(text) is string renamed && renamed != text && text.Contains("mode: judge", StringComparison.Ordinal))
                {
                    writes.Add(new PlannedWrite(Templates.WorkflowPath, path, text, renamed));
                    return JudgeWorkflowUpdate.Planned;
                }

                return JudgeWorkflowUpdate.Unchanged;
        }
    }

    /// <summary>
    /// The CI judge's workflow brought to this engine's template (<see cref="JudgeWorkflowRefresh"/>), with the diff shown first. A
    /// workflow edited outside what Lockflight writes is left as it is: the diff to this engine's is shown, and outside a dry run
    /// written to <c>.lockflight/local/migration/</c> for a person to merge.
    /// </summary>
    private async Task<JudgeWorkflowRefreshStatus> PlanWorkflowRefreshAsync(
        string root, string path, string text, List<PlannedWrite> writes, bool dryRun, CancellationToken cancellationToken)
    {
        JudgeWorkflowRefreshPlan refresh = JudgeWorkflowRefresh.Plan(text);
        switch (refresh.Status)
        {
            case JudgeWorkflowRefreshStatus.Outdated:
                PlannedWrite write = new(Templates.WorkflowPath, path, text, refresh.Replacement);
                writes.Add(write);
                await context.Output.WriteLineAsync(
                    $"CI workflow: {Templates.WorkflowPath} is a CI judge's workflow as Lockflight wrote it, so update writes this engine's (its action pin, steps and jobs), keeping its .NET channels, trigger, runners and workspace shell clone{(dryRun ? "." : ":")}");
                if (!dryRun)
                {
                    await context.Output.WriteLineAsync(await write.DiffAsync(cancellationToken));
                }

                break;
            case JudgeWorkflowRefreshStatus.Edited:
                string current = CiStepMigration.Migrate(text) ?? text;
                string proposal = Path.Combine(root, ".lockflight", "local", "migration", Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
                string where = dryRun ? string.Empty : $"; this engine's version is in {Path.GetRelativePath(root, proposal).Replace('\\', '/')}";
                await context.Output.WriteLineAsync(
                    $"note: {Templates.WorkflowPath} was edited outside the lines Lockflight writes, so update leaves it as it is{where}. Merge what you need by hand; it would change so:");
                await context.Output.WriteLineAsync(await new PlannedWrite(Templates.WorkflowPath, path, current, refresh.Replacement).DiffAsync(cancellationToken));
                if (!dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(proposal)!);
                    await File.WriteAllTextAsync(proposal, refresh.Replacement, cancellationToken);
                }

                break;
        }

        return refresh.Status;
    }

    /// <summary>
    /// The Claude Code settings Lockflight wrote into, brought to what <c>init</c> writes now: the native level's hooks, an
    /// <c>Edit</c> deny rule for every protected path (counting the files this update writes), and, where the sandbox write
    /// denials are listed (not <c>init --no-sandbox</c>), the missing ones. Rules are only added, never removed; the plugin's entries
    /// are left as they are (a person may have turned the plugin off). Settings that hold nothing Lockflight writes (the CI-only
    /// level) and settings that cannot be merged into are left alone.
    /// </summary>
    private async Task PlanClaudeSettingsAsync(string root, Manifest manifest, List<PlannedWrite> writes, CancellationToken cancellationToken)
    {
        string settingsPath = Path.Combine(root, ClaudeSettingsMerger.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(settingsPath))
        {
            return;
        }

        string settingsText = await File.ReadAllTextAsync(settingsPath, cancellationToken);
        bool native = ClaudeSettingsMerger.Problem(settingsText, nativeHooks: false) is null && NativeHooks.AnyPresent(JsonNode.Parse(settingsText));
        if (ClaudeSettingsMerger.Problem(settingsText, native) is string problem)
        {
            await context.Output.WriteLineAsync($"note: {ClaudeSettingsMerger.RelativePath} {problem}, so update leaves it as it is; fix it and run update --project again");
            return;
        }

        JsonObject settings = JsonNode.Parse(settingsText)!.AsObject();
        IReadOnlyList<string> globs = DefaultProtectedPaths.For(manifest);
        string[] planned = [.. writes.Where(write => write.NewText is not null).Select(write => write.RelativePath)];
        IReadOnlyList<string> denyRules = PermissionDenyRules.For(root, globs, planned);
        bool lockflights = native || ClaudeSettingsMerger.PluginEnabled(settings) || PermissionDenyRules.Entries(settings).Any(denyRules.Contains);
        if (!lockflights)
        {
            return;
        }

        bool changed = native && NativeHooks.Merge(settings);
        changed |= PermissionDenyRules.Merge(settings, denyRules);
        if (SandboxSettings.Entries(settings, SandboxSettings.DenyWriteKey).Count > 0)
        {
            changed |= SandboxSettings.Merge(settings, SandboxSettings.DenyWrite(root, globs, planned));
        }

        if (changed)
        {
            writes.Add(new PlannedWrite(ClaudeSettingsMerger.RelativePath, settingsPath, settingsText, JsonFiles.Format(settings)));
        }
    }

    /// <summary>
    /// The pipelines <c>init --ci-platform</c> writes for Azure Pipelines, Bitbucket Pipelines and GitLab are not brought to the
    /// current template: only the GitHub workflow is. <see cref="CiStepMigration"/> still moves the lines it moves there (the engine
    /// install step, the policy restore, the base). Each file Lockflight wrote is named, with where the current template is.
    /// </summary>
    private async Task NoteOtherCiPlatformsAsync(string root, CancellationToken cancellationToken)
    {
        foreach ((string relative, string platform) in OtherCiFiles)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path) && (await File.ReadAllTextAsync(path, cancellationToken)).Contains("lockflight", StringComparison.Ordinal))
            {
                await context.Output.WriteLineAsync(
                    $"note: {relative} ({platform}) is not migrated: update brings only the GitHub workflow to this engine's template. Here it updates only the lines it has always moved (the engine install step between its markers, the policy restore and the pull request's base); compare the rest with the current template: {CiPipeline.GuideUrl}");
            }
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

        IReadOnlyList<string> windowsTests = WindowsTargeting.WindowsTestProjects(BuildScope.OfRepository(root));
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
