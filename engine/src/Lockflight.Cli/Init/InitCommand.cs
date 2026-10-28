using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Projects;
using Lockflight.Cli.Proposals;
using Lockflight.Cli.Quality;

namespace Lockflight.Cli.Init;

/// <summary>
/// <c>lockflight init</c> — lite or standard profile onboarding: manifest, tool pin, git-ignored local state, Claude Code plugin settings and one
/// CI job; then links the engine. Idempotent: existing content is kept and merged into, never overwritten. As a sanctioned
/// writer of protected files it records every change in the ledger.
/// </summary>
internal sealed class InitCommand(CliContext context)
{
    /// <summary><c>[--profile lite|standard] [--ci pr|manual|none] [--ci-platform github|azure|bitbucket|gitlab] [--quality] [--architecture-tests] [--no-link] [--no-sandbox]</c> in any order.</summary>
    public async Task<ExitCode> RunAsync(string[] options, CancellationToken cancellationToken)
    {
        bool link = true;
        bool sandbox = true;
        InitProfile profile = InitProfile.Lite;
        CiTrigger? requestedCi = null;
        CiPlatform platform = CiPlatform.GitHub;
        InitQuality quality = new(Analyzers: false, ArchitectureTests: false);
        for (int index = 0; index < options.Length; index++)
        {
            switch (options[index])
            {
                case "--no-link":
                    link = false;
                    break;
                case "--no-sandbox":
                    sandbox = false;
                    break;
                case "--quality":
                    quality = quality with { Analyzers = true };
                    break;
                case "--architecture-tests":
                    quality = quality with { ArchitectureTests = true };
                    break;
                case "--profile" when index + 1 < options.Length:
                    string name = options[++index];
                    if (!InitProfileNames.TryParse(name, out profile))
                    {
                        return await RefuseAsync($"unknown profile '{name}'; use lite or standard");
                    }

                    break;
                case "--profile":
                    return await RefuseAsync("--profile needs a value: lite or standard");
                case "--ci" when index + 1 < options.Length:
                    string trigger = options[++index];
                    if (!CiTriggerNames.TryParse(trigger, out CiTrigger parsedCi))
                    {
                        return await RefuseAsync($"unknown CI trigger '{trigger}'; use pr, manual or none");
                    }

                    requestedCi = parsedCi;

                    break;
                case "--ci":
                    return await RefuseAsync("--ci needs a value: pr, manual or none");
                case "--ci-platform" when index + 1 < options.Length:
                    string platformName = options[++index];
                    if (!CiPlatformNames.TryParse(platformName, out platform))
                    {
                        return await RefuseAsync($"unknown CI platform '{platformName}'; use {CiPlatformNames.Choices}");
                    }

                    break;
                case "--ci-platform":
                    return await RefuseAsync($"--ci-platform needs a value: {CiPlatformNames.Choices}");
                default:
                    return await RefuseAsync($"unknown init option '{options[index]}'; use --profile lite|standard, --ci pr|manual|none, --ci-platform github|azure|bitbucket|gitlab, --quality, --architecture-tests, --no-link or --no-sandbox");
            }
        }

        return await InitializeAsync(link, sandbox, profile, requestedCi, platform, quality, cancellationToken);
    }

    private async Task<ExitCode> InitializeAsync(
        bool link, bool sandbox, InitProfile profile, CiTrigger? requestedCi, CiPlatform platform, InitQuality quality, CancellationToken cancellationToken)
    {
        // The pin init writes is this engine's version: a newer pin is refused below and an older one is raised to it.
        bool developmentPin = EngineVersion.IsDevelopmentBuild(ToolPin.RunningVersion);
        CiTrigger ci = requestedCi ?? CiTriggerDefault.For(ToolPin.RunningVersion);
        string root = ProjectRoot();
        if (await PreflightProblemAsync(root, cancellationToken) is string problem)
        {
            return await RefuseAsync($"{problem}; nothing was written");
        }

        IReadOnlyList<LayerProject> layers = quality.ArchitectureTests ? ArchitectureTestProject.Layers(root) : [];
        if (quality.ArchitectureTests && ArchitectureTestProject.Problem(layers) is string layerProblem)
        {
            return await RefuseAsync($"{layerProblem}; nothing was written");
        }

        string[] testFrameworks = [.. ProjectFiles.TestProjects(root).SelectMany(project => ProjectFiles.TargetFrameworks(root, project)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        IReadOnlyList<string> dotnetChannels = DotnetChannels.For(testFrameworks);
        // A workspace shell: the child directories that look like units go into a new manifest, and the child
        // repositories into the shell's .gitignore so the shell's git never tracks their files.
        IReadOnlyList<UnitCandidate> units = UnitDiscovery.Find(root);
        string manifest = units.Count == 0 ? Templates.Manifest(profile) : ManifestListEditor.Append(Templates.Manifest(profile), "units", [.. units.Select(unit => unit.ManifestEntry)]) ?? Templates.Manifest(profile);
        string[] childRepositories = [.. units.Where(unit => unit.OwnRepository).Select(unit => unit.Path + "/")];
        List<InitStep> steps =
        [
            await WriteIfMissingAsync(root, Manifest.FileName, manifest, cancellationToken),
            await TransformAsync(root, ToolManifestWriter.RelativePathIn(root), ToolManifestWriter.Pin, cancellationToken),
            await TransformAsync(root, ".gitignore", existing => GitIgnoreUpdater.Update(existing, childRepositories), cancellationToken),
        ];
        List<InitStep> ciSteps = [];
        if (ci != CiTrigger.None)
        {
            foreach (CiFile file in CiPipeline.Files(platform, root, dotnetChannels, ci, developmentPin))
            {
                ciSteps.Add(await WriteIfMissingAsync(root, file.RelativePath, file.Content, cancellationToken));
            }
        }

        steps.AddRange(ciSteps);
        List<QualityFile> proposals = [];
        if (quality.Analyzers)
        {
            await AddQualityPackAsync(root, steps, proposals, cancellationToken);
        }

        string? architectureProject = null;
        if (quality.ArchitectureTests)
        {
            architectureProject = await AddArchitectureTestsAsync(root, layers, steps, proposals, cancellationToken);
        }

        if (profile == InitProfile.Standard)
        {
            steps.Add(await TransformAsync(root, ".gitattributes", GitAttributesUpdater.Update, cancellationToken));
        }

        // Last, so the deny rules and the sandbox write denials cover every protected file init has just written (the CI workflow included).
        steps.Add(await TransformAsync(
            root, ClaudeSettingsMerger.RelativePath, existing => ClaudeSettingsMerger.Merge(existing, DenyRules(root), sandbox ? SandboxDenials(root) : null), cancellationToken));

        foreach (InitStep step in steps)
        {
            await context.Output.WriteLineAsync($"{Describe(step.Outcome),-10} {step.RelativePath}");
        }

        if (ciSteps.FirstOrDefault() is InitStep pipeline)
        {
            await ExplainDotnetChannelsAsync(pipeline, testFrameworks, dotnetChannels);
            if (pipeline.Outcome == FileOutcome.Created && developmentPin)
            {
                await ExplainPreviewCiAsync(pipeline.RelativePath, ci);
            }

            foreach (string line in CiPipeline.NextSteps(platform, root, ciSteps))
            {
                await context.Output.WriteLineAsync(line);
            }
        }

        await RecordAsync(root, steps, cancellationToken);
        if (proposals.Count > 0 && await ProposeAsync(root, proposals, cancellationToken) is ExitCode proposeFailure)
        {
            return proposeFailure;
        }

        foreach (string line in QualityNextSteps.For(quality, steps, proposals, architectureProject))
        {
            await context.Output.WriteLineAsync(line);
        }

        if (!link)
        {
            return ExitCode.Pass;
        }

        CliContext rootContext = context with { WorkingDirectory = root };
        ExitCode linkResult = await new LinkCommand(rootContext).RunAsync(cancellationToken);
        if (linkResult == ExitCode.Pass)
        {
            bool hasSpecKit = Directory.Exists(Path.Combine(root, ".specify"));
            string? preset = profile == InitProfile.Standard && !hasSpecKit ? PresetLocator.Find(context.Environment.Get(EnvironmentVariables.Home), root) : null;
            foreach (string line in InitNextSteps.For(profile, hasSpecKit, preset))
            {
                await context.Output.WriteLineAsync(line);
            }
        }

        return linkResult;
    }

    /// <summary>
    /// The quality pack's files: one that does not exist yet is written, as init writes every protected file it starts; one the
    /// team already has is only proposed, because init never changes the team's own content.
    /// </summary>
    private static async Task AddQualityPackAsync(string root, List<InitStep> steps, List<QualityFile> proposals, CancellationToken cancellationToken)
    {
        foreach (QualityFile file in QualityPack.Files(root))
        {
            if (file.Existing)
            {
                proposals.Add(file);
            }
            else
            {
                steps.Add(await WriteIfMissingAsync(root, file.RelativePath, file.Content, cancellationToken));
            }
        }
    }

    /// <summary>The architecture test project, unless it exists; a central package list that lacks its packages is proposed.</summary>
    private static async Task<string> AddArchitectureTestsAsync(
        string root, IReadOnlyList<LayerProject> layers, List<InitStep> steps, List<QualityFile> proposals, CancellationToken cancellationToken)
    {
        string directory = ArchitectureTestProject.RelativeDirectory(root, layers);
        string project = $"{directory}/{ArchitectureTestProject.Name(layers)}.csproj";
        bool centralPackages = ArchitectureTestProject.UsesCentralPackages(root);
        steps.Add(await WriteIfMissingAsync(root, project, ArchitectureTestProject.ProjectFile(root, directory, layers, centralPackages), cancellationToken));
        steps.Add(await WriteIfMissingAsync(root, $"{directory}/{ArchitectureTestProject.TestFileName}", ArchitectureTestProject.TestFile(layers), cancellationToken));
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
            ExitCode proposed = await new ProposeCommand(context with { WorkingDirectory = root }).RunAsync([.. arguments], cancellationToken);
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
    private static async Task<string?> PreflightProblemAsync(string root, CancellationToken cancellationToken)
    {
        string toolManifest = ToolManifestWriter.RelativePathIn(root);
        string toolManifestPath = Path.Combine(root, toolManifest);
        string? tools = File.Exists(toolManifestPath) ? await File.ReadAllTextAsync(toolManifestPath, cancellationToken) : null;
        if (ToolManifestWriter.Problem(tools) is string toolsProblem)
        {
            return $"{toolManifest} {toolsProblem}";
        }

        string settingsPath = Path.Combine(root, ClaudeSettingsMerger.RelativePath);
        string? settings = File.Exists(settingsPath) ? await File.ReadAllTextAsync(settingsPath, cancellationToken) : null;
        return ClaudeSettingsMerger.Problem(settings) is string settingsProblem
            ? $"{ClaudeSettingsMerger.RelativePath} {settingsProblem}; fix it by hand (keep your settings) and run init again"
            : null;
    }

    /// <summary>The protected paths of the manifest just written (built-in, per unit and its own) as sandbox write denials.</summary>
    private static IReadOnlyList<string> SandboxDenials(string root) => SandboxSettings.DenyWrite(root, ProtectedGlobs(root));

    /// <summary>The same protected paths as <c>Edit</c> deny rules.</summary>
    private static IReadOnlyList<string> DenyRules(string root) => PermissionDenyRules.For(root, ProtectedGlobs(root));

    private static IReadOnlyList<string> ProtectedGlobs(string root) =>
        ManifestLocator.Discover(root).Manifest is Manifest manifest ? DefaultProtectedPaths.For(manifest) : DefaultProtectedPaths.Globs;

    /// <summary>The nearest existing Lockflight project, else the current directory.</summary>
    private string ProjectRoot() =>
        ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;

    private static async Task<InitStep> WriteIfMissingAsync(string root, string relativePath, string content, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, relativePath);
        if (File.Exists(path))
        {
            return new InitStep(relativePath, FileOutcome.Unchanged);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return new InitStep(relativePath, FileOutcome.Created);
    }

    private static async Task<InitStep> TransformAsync(
        string root, string relativePath, Func<string?, (string Content, bool Changed)> transform, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, relativePath);
        string? existing = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
        (string content, bool changed) = transform(existing);
        if (!changed)
        {
            return new InitStep(relativePath, FileOutcome.Unchanged);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return new InitStep(relativePath, existing is null ? FileOutcome.Created : FileOutcome.Updated);
    }

    private async Task RecordAsync(string root, List<InitStep> steps, CancellationToken cancellationToken)
    {
        LedgerWriter ledger = new(root, context.Time);
        foreach (InitStep step in steps.Where(step => step.Outcome != FileOutcome.Unchanged))
        {
            await ledger.AppendAsync(new LedgerEvent(LedgerEventType.ConfigWritten, $"init:{step.RelativePath}", Describe(step.Outcome), null), cancellationToken);
        }
    }

    private async Task<ExitCode> RefuseAsync(string message)
    {
        await context.Error.WriteLineAsync($"lockflight: {message}");
        return ExitCode.Config;
    }

    private static string Describe(FileOutcome outcome) => outcome switch
    {
        FileOutcome.Created => "created",
        FileOutcome.Updated => "updated",
        _ => "unchanged",
    };
}
