using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Projects;
using Lockflight.Cli.Proposals;
using Lockflight.Cli.Quality;

namespace Lockflight.Cli.Init;

/// <summary>
/// <c>lockflight init</c> — lite or standard profile onboarding: manifest, tool pin, git-ignored local state, Claude Code plugin settings,
/// the Lockflight block in <c>CLAUDE.md</c> and one CI job; then links the engine. It also adopts an existing Claude Code and Spec Kit
/// setup (<see cref="InitAdoption"/>): the team's hooks, skills and agents are kept and protected, existing features are marked as
/// predating Lockflight. Idempotent: existing content is kept and merged into, never overwritten. <c>--dry-run</c> prints the plan and
/// writes nothing; git is the undo (commit init's changes as one commit, <c>git revert</c> it). As a sanctioned writer of protected
/// files it records every change in the ledger.
/// </summary>
internal sealed class InitCommand(CliContext context) : ICommand
{
    private const string Commit = "Review init's changes and commit them as one commit; to undo init, `git revert` that commit.";

    private const string Choices = "--profile lite|standard, --preset relaxed|default|strict, --ci pr|manual|none, --ci-platform github|azure|bitbucket|gitlab, --quality, --architecture-tests, --dry-run, --unit-of <shell url> --unit-path <path>, --speckit-preset <dir>, --native or --ci-only, --no-link or --no-sandbox";

    /// <summary>
    /// <c>[--profile lite|standard] [--preset relaxed|default|strict] [--ci pr|manual|none] [--ci-platform github|azure|bitbucket|gitlab] [--quality] [--architecture-tests]
    /// [--dry-run] [--unit-of url --unit-path path] [--speckit-preset dir] [--native | --ci-only] [--no-link] [--no-sandbox]</c> in any order.
    /// </summary>
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        string[] options = arguments.RestArray;
        InitOptions init = new(Link: true, Sandbox: true, InitProfile.Lite, RequestedCi: null, CiPlatform.GitHub, new InitQuality(Analyzers: false, ArchitectureTests: false), DryRun: false, Shell: null, SpecKitPreset: null);
        string? shellUrl = null;
        string? unitPath = null;
        for (int index = 0; index < options.Length; index++)
        {
            string option = options[index];
            bool hasValue = index + 1 < options.Length;
            switch (option)
            {
                case "--no-link":
                    init = init with { Link = false };
                    break;
                case "--no-sandbox":
                    init = init with { Sandbox = false };
                    break;
                case "--dry-run":
                    init = init with { DryRun = true };
                    break;
                case "--native" or "--ci-only" when init.Level != InstallLevel.Plugin && init.Level != LevelOf(option):
                    return await RefuseAsync("--native and --ci-only are different install levels; choose one");
                case "--native" or "--ci-only":
                    init = init with { Level = LevelOf(option) };
                    break;
                case "--quality":
                    init = init with { Quality = init.Quality with { Analyzers = true } };
                    break;
                case "--architecture-tests":
                    init = init with { Quality = init.Quality with { ArchitectureTests = true } };
                    break;
                case "--profile" when hasValue:
                    string name = options[++index];
                    if (!InitProfileNames.TryParse(name, out InitProfile profile))
                    {
                        return await RefuseAsync($"unknown profile '{name}'; use lite or standard");
                    }

                    init = init with { Profile = profile };
                    break;
                case "--preset" when hasValue:
                    string presetName = options[++index];
                    if (!RoutingPresetNames.TryParse(presetName, out RoutingPreset preset))
                    {
                        return await RefuseAsync($"unknown preset '{presetName}'; use {RoutingPresetNames.Choices}");
                    }

                    init = init with { Preset = preset };
                    break;
                case "--ci" when hasValue:
                    string trigger = options[++index];
                    if (!CiTriggerNames.TryParse(trigger, out CiTrigger parsedCi))
                    {
                        return await RefuseAsync($"unknown CI trigger '{trigger}'; use pr, manual or none");
                    }

                    init = init with { RequestedCi = parsedCi };
                    break;
                case "--ci-platform" when hasValue:
                    string platformName = options[++index];
                    if (!CiPlatformNames.TryParse(platformName, out CiPlatform platform))
                    {
                        return await RefuseAsync($"unknown CI platform '{platformName}'; use {CiPlatformNames.Choices}");
                    }

                    init = init with { Platform = platform };
                    break;
                case "--unit-of" when hasValue:
                    shellUrl = options[++index];
                    break;
                case "--unit-path" when hasValue:
                    unitPath = UnitsSectionReader.UnitPath(options[++index]);
                    if (unitPath is null || !unitPath.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '/'))
                    {
                        return await RefuseAsync("--unit-path takes this repository's path in the shell's units:, relative, of letters, digits, '.', '_', '-' and '/', without '.' or '..' segments");
                    }

                    break;
                case "--speckit-preset" when hasValue:
                    init = init with { SpecKitPreset = options[++index] };
                    break;
                case "--profile":
                    return await RefuseAsync("--profile needs a value: lite or standard");
                case "--preset":
                    return await RefuseAsync($"--preset needs a value: {RoutingPresetNames.Choices}");
                case "--ci":
                    return await RefuseAsync("--ci needs a value: pr, manual or none");
                case "--ci-platform":
                    return await RefuseAsync($"--ci-platform needs a value: {CiPlatformNames.Choices}");
                case "--unit-of" or "--unit-path" or "--speckit-preset":
                    return await RefuseAsync($"{option} needs a value");
                default:
                    return await RefuseAsync($"unknown init option '{option}'; use {Choices}");
            }
        }

        if (init is { Level: InstallLevel.CiOnly, RequestedCi: CiTrigger.None })
        {
            return await RefuseAsync("--ci-only sets Lockflight up for CI alone, so it needs a CI file: drop --ci none");
        }

        if ((unitPath is null) != (shellUrl is null))
        {
            return await RefuseAsync("--unit-of <shell url> and --unit-path <path> go together: the shell's clone URL and this repository's path among its units");
        }

        // CI only links no engine: nothing on the machine runs it.
        init = init.Level == InstallLevel.CiOnly ? init with { Link = false } : init;
        return await InitializeAsync(shellUrl is null ? init : init with { Shell = new ShellSettings(shellUrl, Ref: null, unitPath!) }, cancellationToken);
    }

    /// <summary>Whether the project's or the user's Claude Code settings enable the plugin; unreadable settings count as not.</summary>
    private bool PluginAlsoEnabled(string root)
    {
        string? home = context.Environment.HomeDirectory();
        foreach (string path in new[] { Path.Combine(root, ClaudeSettingsMerger.RelativePath), home is null ? null : Path.Combine(home, ".claude", "settings.json") }.OfType<string>())
        {
            try
            {
                if (File.Exists(path) && ClaudeSettingsMerger.PluginEnabled(JsonNode.Parse(File.ReadAllText(path))))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                // Unreadable settings enable nothing.
            }
        }

        return false;
    }

    private static InstallLevel LevelOf(string option) => option == "--native" ? InstallLevel.Native : InstallLevel.CiOnly;

    private async Task<ExitCode> InitializeAsync(InitOptions init, CancellationToken cancellationToken)
    {
        // The pin init writes is this engine's version: a newer pin is refused below and an older one is raised to it.
        bool developmentPin = EngineVersion.IsDevelopmentBuild(ToolPin.RunningVersion);
        CiTrigger ci = init.RequestedCi ?? CiTriggerDefault.For(ToolPin.RunningVersion);
        string root = ProjectRoot();
        bool write = !init.DryRun;
        if (await PreflightProblemAsync(root, init.Level, cancellationToken) is string problem)
        {
            return await RefuseAsync($"{problem}; nothing was written");
        }

        string manifestPath = Path.Combine(root, Manifest.FileName);
        string? existingManifestText = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath, cancellationToken) : null;
        Manifest? existingManifest = existingManifestText is null ? null : ManifestLocator.Discover(root).Manifest;
        // Only the very first init marks features: no manifest of either kind and no Lockflight state yet. A later init (which the
        // agent may run) must never exempt a feature from approval.
        bool firstInit = existingManifestText is null && ManifestLocator.Discover(root).Status == ManifestLoadStatus.NotFound
            && !Directory.Exists(Path.Combine(root, ".lockflight"));
        if (existingManifestText is not null && existingManifest is null)
        {
            return await RefuseAsync($"{Manifest.FileName} does not load; fix it first (lockflight doctor shows the error); nothing was written");
        }

        if (InitAdoption.ShellProblem(existingManifest, init.Shell) is string shellProblem)
        {
            return await RefuseAsync($"--unit-of: {shellProblem}; nothing was written");
        }

        if (init.SpecKitPreset is string requested && HumanOnlyCommands.RunsUnderAgent(context.Environment))
        {
            // The upgrade rewrites the team's protected Spec Kit skills and runs a preset from the directory named: a person's decision.
            string line = $"cd {ShellQuoting.Quote(root)} && {EngineCommand.Linked} init --speckit-preset {ShellQuoting.Quote(Path.GetFullPath(requested, context.WorkingDirectory))}";
            await context.Error.WriteLineAsync($"lockflight: 'init --speckit-preset' upgrades the team's Spec Kit and is a person's decision, so it does not run inside a Claude Code session. Ask the user to {HumanOnlyCommands.OpenATerminalAndRun(line)}.");
            return ExitCode.HookBlock;
        }

        if (init.SpecKitPreset is string presetOption && !Directory.Exists(Path.GetFullPath(presetOption, context.WorkingDirectory)))
        {
            return await RefuseAsync($"--speckit-preset: '{presetOption}' is not a directory; nothing was written");
        }

        IReadOnlyList<LayerProject> layers = init.Quality.ArchitectureTests ? ArchitectureTestProject.Layers(root) : [];
        if (init.Quality.ArchitectureTests && ArchitectureTestProject.Problem(layers) is string layerProblem)
        {
            return await RefuseAsync($"{layerProblem}; nothing was written");
        }

        AdoptionPlan plan = SetupInventory.Build(root, existingManifest, context.Environment.Get(EnvironmentVariables.Home), context.Time.GetUtcNow());
        // A workspace shell: the child directories that look like units go into a new manifest, and the child
        // repositories into the shell's .gitignore so the shell's git never tracks their files.
        IReadOnlyList<UnitCandidate> units = init.Shell is null ? UnitDiscovery.Find(root) : [];
        ManifestPlan manifest = InitAdoption.Manifest(existingManifestText, existingManifest, init.Profile, init.Preset, plan, units, init.Shell);
        ManifestLoadResult plannedLoad = new ManifestParser(root, manifestPath).Parse(manifest.Content);
        if (plannedLoad.Manifest is not Manifest planned)
        {
            return await RefuseAsync($"the {Manifest.FileName} init would write does not load ({plannedLoad.Error?.Message}); nothing was written");
        }

        IReadOnlyList<string> globs = DefaultProtectedPaths.For(planned);
        string[] testFrameworks = [.. ProjectFiles.TestProjects(root).SelectMany(project => ProjectFiles.TargetFrameworks(root, project)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        IReadOnlyList<string> dotnetChannels = DotnetChannels.For(testFrameworks);
        string[] childRepositories = [.. units.Where(unit => unit.OwnRepository).Select(unit => unit.Path + "/")];
        List<InitStep> steps =
        [
            await TransformAsync(root, Manifest.FileName, _ => (manifest.Content, manifest.Changed), write, cancellationToken),
            await TransformAsync(root, ToolManifestWriter.RelativePathIn(root), ToolManifestWriter.Pin, write, cancellationToken),
            await TransformAsync(root, ".gitignore", existing => GitIgnoreUpdater.Update(existing, childRepositories), write, cancellationToken),
        ];
        if (init.Level != InstallLevel.CiOnly)
        {
            steps.Add(await TransformAsync(root, ClaudeMdBlock.FileName, existing => ClaudeMdBlock.IsCurrent(existing ?? string.Empty) ? (existing!, false) : (ClaudeMdBlock.Upsert(existing ?? string.Empty), true), write, cancellationToken));
        }

        steps.AddRange(await EngineLockStepsAsync(root, write, cancellationToken));
        IReadOnlyList<string> predating = firstInit ? InitAdoption.PredatingFeatures(plan) : [];
        if (predating.Count > 0)
        {
            steps.Add(MarkPredating(root, predating, write));
        }

        List<InitStep> ciSteps = [];
        if (ci != CiTrigger.None)
        {
            foreach (CiFile pipelineFile in CiPipeline.Files(init.Platform, root, dotnetChannels, ci, developmentPin))
            {
                CiFile file = init.Shell is null ? pipelineFile : CiShellPointer.ForUnit(pipelineFile, init.Platform);
                ciSteps.Add(await WriteIfMissingAsync(root, file.RelativePath, file.Content, write, cancellationToken));
            }
        }

        steps.AddRange(ciSteps);
        List<QualityFile> proposals = [];
        if (init.Quality.Analyzers)
        {
            await AddQualityPackAsync(root, steps, proposals, write, cancellationToken);
        }

        string? architectureProject = null;
        if (init.Quality.ArchitectureTests)
        {
            architectureProject = await AddArchitectureTestsAsync(root, layers, steps, proposals, write, cancellationToken);
        }

        if (init.Profile == InitProfile.Standard)
        {
            steps.Add(await TransformAsync(root, ".gitattributes", GitAttributesUpdater.Update, write, cancellationToken));
        }

        if (init.Level == InstallLevel.Native)
        {
            steps.Add(await TransformAsync(root, NativeHookShim.RelativePath, existing => (NativeHookShim.Text, existing != NativeHookShim.Text), write, cancellationToken));
            steps.Add(await TransformAsync(root, ".gitattributes", GitAttributesUpdater.UpdateHookShim, write, cancellationToken));
        }

        // Last, so the deny rules and the sandbox write denials cover every protected file init has just written (the CI workflow
        // and the native hook shim included). CI only writes nothing for Claude Code.
        if (init.Level != InstallLevel.CiOnly)
        {
            steps.Add(await TransformAsync(
                root, ClaudeSettingsMerger.RelativePath,
                existing => ClaudeSettingsMerger.Merge(existing, PermissionDenyRules.For(root, globs), init.Sandbox ? SandboxSettings.DenyWrite(root, globs) : null, init.Level),
                write, cancellationToken));
        }

        if (init.DryRun)
        {
            return await DryRunAsync(root, plan, steps, proposals, manifest, predating);
        }

        foreach (InitStep step in steps)
        {
            await context.Output.WriteLineAsync($"{Describe(step.Outcome),-10} {step.RelativePath}");
        }

        if (init.Level == InstallLevel.Native && PluginAlsoEnabled(root))
        {
            await context.Output.WriteLineAsync(
                $"note: the Lockflight plugin is enabled too (project or user settings), so the guard and the Stop gates would run twice; disable it for this project (\"enabledPlugins\": {{ \"{Templates.PluginId}\": false }} in .claude/settings.local.json)");
        }

        List<string> done = [];
        List<string> manual = [.. manifest.Manual];
        string? preset = init.SpecKitPreset is string presetDirectory ? Path.GetFullPath(presetDirectory, context.WorkingDirectory) : null;
        if (InitAdoption.ChangesSpecKit(plan))
        {
            if (preset is not null)
            {
                await InitAdoption.AdoptSpecKitAsync(root, plan, preset, done, manual, cancellationToken);
            }
            else
            {
                manual.Add(InitAdoption.SpecKitLine(root, PresetLocator.Find(context.Environment.Get(EnvironmentVariables.Home), root)));
            }
        }

        foreach (string line in InitAdoption.Notes(plan).Concat(done.Select(line => $"done       {line}")).Concat(manual.Select(line => $"manual     {line}")))
        {
            await context.Output.WriteLineAsync(line);
        }

        if (ciSteps.FirstOrDefault() is InitStep pipeline)
        {
            await ExplainDotnetChannelsAsync(pipeline, testFrameworks, dotnetChannels);
            if (pipeline.Outcome == FileOutcome.Created && developmentPin)
            {
                await ExplainPreviewCiAsync(pipeline.RelativePath, ci);
            }

            foreach (string line in CiPipeline.NextSteps(init.Platform, root, ciSteps))
            {
                await context.Output.WriteLineAsync(line);
            }
        }

        await RecordAsync(root, steps, done, cancellationToken);
        if (proposals.Count > 0 && await ProposeAsync(root, proposals, cancellationToken) is ExitCode proposeFailure)
        {
            return proposeFailure;
        }

        foreach (string line in QualityNextSteps.For(init.Quality, steps, proposals, architectureProject))
        {
            await context.Output.WriteLineAsync(line);
        }

        await context.Output.WriteLineAsync(Commit);
        if (!init.Link)
        {
            return ExitCode.Pass;
        }

        CliContext rootContext = context with { WorkingDirectory = root };
        ExitCode linkResult = await new LinkCommand(rootContext).RunAsync(cancellationToken);
        if (linkResult == ExitCode.Pass)
        {
            SpecKitSetup specKit = !Directory.Exists(Path.Combine(root, ".specify")) ? SpecKitSetup.None
                : LockflightPresetRegistry.IsInstalled(root) ? SpecKitSetup.WithPreset
                : SpecKitSetup.WithoutPreset;
            string? foundPreset = init.Profile == InitProfile.Standard && specKit != SpecKitSetup.WithPreset ? PresetLocator.Find(context.Environment.Get(EnvironmentVariables.Home), root) : null;
            foreach (string line in InitNextSteps.For(init.Profile, specKit, foundPreset))
            {
                await context.Output.WriteLineAsync(line);
            }
        }

        return linkResult;
    }

    /// <summary>What init would write and what it would adopt, with nothing written: the plan a person reviews before running init.</summary>
    private async Task<ExitCode> DryRunAsync(string root, AdoptionPlan plan, List<InitStep> steps, List<QualityFile> proposals, ManifestPlan manifest, IReadOnlyList<string> predating)
    {
        TextWriter output = context.Output;
        await output.WriteLineAsync($"init --dry-run: nothing was written. Running init in {root} would:");
        foreach (InitStep step in steps)
        {
            await output.WriteLineAsync($"{DescribePlanned(step.Outcome),-16} {step.RelativePath}");
        }

        foreach (QualityFile proposal in proposals)
        {
            await output.WriteLineAsync($"{"propose",-16} {proposal.RelativePath} (exists: a patch for you to apply)");
        }

        foreach (string line in manifest.Manual)
        {
            await output.WriteLineAsync($"{"manual",-16} {line}");
        }

        if (predating.Count > 0)
        {
            await output.WriteLineAsync($"It marks {predating.Count} existing feature{(predating.Count == 1 ? string.Empty : "s")} as predating Lockflight: {string.Join(", ", predating)}.");
        }

        await output.WriteLineAsync();
        await output.WriteAsync(AdoptionPlanWriter.Markdown(plan));
        await output.WriteLineAsync(Commit);
        return ExitCode.Pass;
    }

    /// <summary>Marks the features that predate Lockflight, so <c>approve verify --base</c> and <c>trace</c> require nothing of them.</summary>
    private static InitStep MarkPredating(string root, IReadOnlyList<string> features, bool write)
    {
        string relative = $"{BaselinePaths.Directory}/{AdoptedFeatures.FileName}";
        AdoptedFeatures adopted = new(root);
        IReadOnlySet<string> already = adopted.Load();
        if (features.All(already.Contains))
        {
            return new InitStep(relative, FileOutcome.Unchanged);
        }

        bool existed = File.Exists(Path.Combine(root, relative));
        if (write)
        {
            adopted.Save(already.Concat(features));
        }

        return new InitStep(relative, existed ? FileOutcome.Updated : FileOutcome.Created);
    }

    /// <summary>
    /// The quality pack's files: one that does not exist yet is written, as init writes every protected file it starts; one the
    /// team already has is only proposed, because init never changes the team's own content.
    /// </summary>
    private static async Task AddQualityPackAsync(string root, List<InitStep> steps, List<QualityFile> proposals, bool write, CancellationToken cancellationToken)
    {
        foreach (QualityFile file in QualityPack.Files(root))
        {
            if (file.Existing)
            {
                proposals.Add(file);
            }
            else
            {
                steps.Add(await WriteIfMissingAsync(root, file.RelativePath, file.Content, write, cancellationToken));
            }
        }
    }

    /// <summary>The architecture test project, unless it exists; a central package list that lacks its packages is proposed.</summary>
    private static async Task<string> AddArchitectureTestsAsync(
        string root, IReadOnlyList<LayerProject> layers, List<InitStep> steps, List<QualityFile> proposals, bool write, CancellationToken cancellationToken)
    {
        string directory = ArchitectureTestProject.RelativeDirectory(root, layers);
        string project = $"{directory}/{ArchitectureTestProject.Name(layers)}.csproj";
        bool centralPackages = ArchitectureTestProject.UsesCentralPackages(root);
        steps.Add(await WriteIfMissingAsync(root, project, ArchitectureTestProject.ProjectFile(root, directory, layers, centralPackages), write, cancellationToken));
        steps.Add(await WriteIfMissingAsync(root, $"{directory}/{ArchitectureTestProject.TestFileName}", ArchitectureTestProject.TestFile(layers), write, cancellationToken));
        string packagesPath = Path.Combine(root, ArchitectureTestProject.CentralPackagesFile);
        if (centralPackages && ArchitectureTestProject.CentralPackages(root, await File.ReadAllTextAsync(packagesPath, cancellationToken)) is string packages)
        {
            proposals.Add(new QualityFile(ArchitectureTestProject.CentralPackagesFile, packages, Existing: true));
        }

        return project;
    }

    /// <summary>
    /// Proposes each change to a file the team already has through <c>lockflight propose</c>: a patch the person reads and
    /// applies, never applied here. The proposed content waits in a temporary directory outside the project.
    /// </summary>
    private async Task<ExitCode?> ProposeAsync(string root, List<QualityFile> proposals, CancellationToken cancellationToken)
    {
        DirectoryInfo staging = Directory.CreateTempSubdirectory("lockflight-init-proposals-");
        try
        {
            List<string> arguments = [];
            for (int index = 0; index < proposals.Count; index++)
            {
                string content = Path.Combine(staging.FullName, $"{index}-{Path.GetFileName(proposals[index].RelativePath)}");
                await File.WriteAllTextAsync(content, proposals[index].Content, cancellationToken);
                arguments.AddRange([proposals[index].RelativePath, "--from", content]);
            }

            await context.Output.WriteLineAsync("These files exist, so init proposes the quality pack's change to them instead of making it:");
            CommandArguments propose = new(CommandRegistry.Match(["propose"])!, arguments);
            ExitCode proposed = await new ProposeCommand(context with { WorkingDirectory = root }).RunAsync(propose, cancellationToken);
            return proposed == ExitCode.Pass ? null : proposed;
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    /// <summary>Says why CI installs more than the engine's SDK, or that an existing pipeline may need it.</summary>
    private async Task ExplainDotnetChannelsAsync(InitStep pipeline, string[] testFrameworks, IReadOnlyList<string> dotnetChannels)
    {
        if (dotnetChannels.Count < 2)
        {
            return;
        }

        string channels = string.Join(", ", dotnetChannels);
        string frameworks = string.Join(", ", testFrameworks);
        await context.Output.WriteLineAsync(pipeline.Outcome == FileOutcome.Created
            ? $"CI installs .NET {channels}: the test projects target {frameworks}, and tests need each runtime. Locally, gates.test.framework in {Manifest.FileName} picks one target when a runtime is missing."
            : $"Note: the test projects target {frameworks}; {pipeline.RelativePath} already existed, so check that it installs .NET {channels}.");
    }

    /// <summary>Says what a development pin means for the pipeline just written: no package feed has the engine it installs.</summary>
    private async Task ExplainPreviewCiAsync(string pipeline, CiTrigger ci) =>
        await context.Output.WriteLineAsync(ci == CiTrigger.Manual
            ? $"CI: {pipeline} runs only when started by hand. This engine is a development build ({ToolPin.RunningVersion}) that no package feed has, so a job that installs it would fail on every pull request. Once the project pins a published release, switch it to pull requests (its comment says how); --ci pr writes the pull-request trigger now."
            : $"Note: {pipeline} runs on every pull request, but this engine is a development build ({ToolPin.RunningVersion}) that no package feed has: the job fails at the engine install until the project pins a published release.");

    /// <summary>What init cannot merge into, checked before it writes anything so a refusal leaves the project as it was.</summary>
    private static async Task<string?> PreflightProblemAsync(string root, InstallLevel level, CancellationToken cancellationToken)
    {
        string toolManifest = ToolManifestWriter.RelativePathIn(root);
        string toolManifestPath = Path.Combine(root, toolManifest);
        string? tools = File.Exists(toolManifestPath) ? await File.ReadAllTextAsync(toolManifestPath, cancellationToken) : null;
        if (ToolManifestWriter.Problem(tools) is string toolsProblem)
        {
            return $"{toolManifest} {toolsProblem}";
        }

        if (level == InstallLevel.CiOnly)
        {
            return null;
        }

        string settingsPath = Path.Combine(root, ClaudeSettingsMerger.RelativePath);
        string? settings = File.Exists(settingsPath) ? await File.ReadAllTextAsync(settingsPath, cancellationToken) : null;
        return ClaudeSettingsMerger.Problem(settings, level == InstallLevel.Native) is string settingsProblem
            ? $"{ClaudeSettingsMerger.RelativePath} {settingsProblem}; fix it by hand (keep your settings) and run init again"
            : null;
    }

    /// <summary>The nearest existing Lockflight project, else the current directory.</summary>
    private string ProjectRoot() =>
        ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;

    /// <param name="write">False for <c>--dry-run</c>: the outcome is worked out and nothing is written.</param>
    private static async Task<InitStep> WriteIfMissingAsync(string root, string relativePath, string content, bool write, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, relativePath);
        if (File.Exists(path))
        {
            return new InitStep(relativePath, FileOutcome.Unchanged);
        }

        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }

        return new InitStep(relativePath, FileOutcome.Created);
    }

    /// <summary>
    /// The engine lock for the running engine and, for a committed lock, the vendored install script; an existing lock is the
    /// project's policy and is left alone (update moves it). Without hashes for this engine the project keeps its version pin.
    /// </summary>
    private async Task<IReadOnlyList<InitStep>> EngineLockStepsAsync(string root, bool write, CancellationToken cancellationToken)
    {
        if (File.Exists(EngineLockFiles.CommittedPath(root)))
        {
            return [new InitStep(EngineLock.RelativePath, FileOutcome.Unchanged)];
        }

        EngineLockPlan plan = await EngineLockPlanner.PlanAsync(
            root, ToolPin.RunningVersion, context.Engine.Path, EnginePackageFeed.FromEnvironment(context.Environment), GitHubReleaseHashes.FetchAsync, context.Processes, cancellationToken);
        if (plan.Lock is not EngineLock engineLock)
        {
            return [];
        }

        string lockRelative = Path.GetRelativePath(root, plan.LockPath!).Replace('\\', '/');
        List<InitStep> steps = [await TransformAsync(root, lockRelative, existing => (EngineLockText.Write(engineLock), existing != EngineLockText.Write(engineLock)), write, cancellationToken)];
        if (plan.Committed)
        {
            string script = Encoding.UTF8.GetString(InstallScript.Bytes);
            steps.Add(await TransformAsync(root, InstallScript.RelativePath, existing => (script, existing != script), write, cancellationToken));
        }

        return steps;
    }

    private static async Task<InitStep> TransformAsync(
        string root, string relativePath, Func<string?, (string Content, bool Changed)> transform, bool write, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, relativePath);
        string? existing = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
        (string content, bool changed) = transform(existing);
        if (!changed)
        {
            return new InitStep(relativePath, FileOutcome.Unchanged);
        }

        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }

        return new InitStep(relativePath, existing is null ? FileOutcome.Created : FileOutcome.Updated);
    }

    private async Task RecordAsync(string root, List<InitStep> steps, List<string> done, CancellationToken cancellationToken)
    {
        LedgerWriter ledger = new(root, context.Time);
        foreach (InitStep step in steps.Where(step => step.Outcome != FileOutcome.Unchanged))
        {
            await ledger.AppendAsync(new LedgerEvent(LedgerEventType.ConfigWritten, $"init:{step.RelativePath}", Describe(step.Outcome), null), cancellationToken);
        }

        if (done.Count > 0)
        {
            await ledger.AppendAsync(new LedgerEvent(LedgerEventType.ConfigWritten, "init:speckit", string.Join("; ", done), null), cancellationToken);
        }
    }

    private async Task<ExitCode> RefuseAsync(string message)
    {
        await context.Error.WriteLineAsync($"lockflight: {message}");
        return ExitCode.Config;
    }

    private static string DescribePlanned(FileOutcome outcome) => outcome switch
    {
        FileOutcome.Created => "would create",
        FileOutcome.Updated => "would update",
        _ => "unchanged",
    };

    private static string Describe(FileOutcome outcome) => outcome switch
    {
        FileOutcome.Created => "created",
        FileOutcome.Updated => "updated",
        _ => "unchanged",
    };
}
